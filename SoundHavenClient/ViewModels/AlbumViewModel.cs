using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SoundHaven.Commands;
using SoundHaven.Models;
using SoundHaven.Services;
using SoundHaven.Stores;

namespace SoundHaven.ViewModels;

/// <summary>
/// Backs the album page: an album header (cover/title/artist/stats) plus its track
/// list fetched from Last.fm. Tracks carry only artist+title and resolve to YouTube
/// on play, exactly like recommendations.
/// </summary>
public sealed class AlbumViewModel : ViewModelBase
{
    private readonly PlaybackViewModel _playbackViewModel;
    private readonly ILastFmDataService _lastFmDataService;
    private readonly IAlbumArtService _albumArtService;
    private readonly IUserNotificationService _notifications;
    private readonly PlaylistStore _playlistStore;
    private readonly IYouTubeMediaService _youTubeMediaService;
    private readonly ObservableCollection<Song> _tracks = new();
    private readonly ObservableCollection<PlaylistTrackRow> _trackRows = new();
    private Song? _album;
    private Song? _menuSong;
    private bool _isLoading;
    private CancellationTokenSource? _loadCancellation;

    public AlbumViewModel(
        PlaybackViewModel playbackViewModel,
        ILastFmDataService lastFmDataService,
        IAlbumArtService albumArtService,
        IUserNotificationService notifications,
        PlaylistStore playlistStore,
        IYouTubeMediaService youTubeMediaService)
    {
        _playbackViewModel = playbackViewModel ?? throw new ArgumentNullException(nameof(playbackViewModel));
        _lastFmDataService = lastFmDataService ?? throw new ArgumentNullException(nameof(lastFmDataService));
        _albumArtService = albumArtService ?? throw new ArgumentNullException(nameof(albumArtService));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _playlistStore = playlistStore ?? throw new ArgumentNullException(nameof(playlistStore));
        _youTubeMediaService = youTubeMediaService ?? throw new ArgumentNullException(nameof(youTubeMediaService));

        PlayAlbumCommand = new AsyncRelayCommand(
            () => PlayTrackAsync(_tracks.FirstOrDefault()),
            () => _tracks.Count > 0,
            exception => _notifications.ShowError(exception.Message));
        ShuffleAlbumCommand = new AsyncRelayCommand(
            ShuffleAlbumAsync,
            () => _tracks.Count > 0,
            exception => _notifications.ShowError(exception.Message));
        PlayTrackCommand = new AsyncRelayCommand<Song>(
            PlayTrackAsync,
            song => song is not null,
            exception => _notifications.ShowError(exception.Message));
        AddAlbumToPlaylistCommand = new RelayCommand<Playlist>(
            AddAlbumToPlaylist,
            playlist => playlist is { Id: > 0 } && _tracks.Count > 0);
        AddSongToPlaylistCommand = new RelayCommand<Playlist>(
            AddSongToPlaylist,
            playlist => playlist is { Id: > 0 });
        DownloadSongCommand = new AsyncRelayCommand<Song>(
            DownloadSongAsync,
            song => song is not null && song.CurrentDownloadState == DownloadState.NotDownloaded,
            exception => _notifications.ShowError(exception.Message));
        OpenSongFolderCommand = new AsyncRelayCommand<Song>(
            ExecuteOpenSongFolderAsync,
            song => song is { FilePath.Length: > 0 },
            exception => _notifications.ShowError(exception.Message));
        ToggleLikeCommand = new RelayCommand<Song>(ToggleLike, song => song is not null);

        _playbackViewModel.PropertyChanged += OnPlaybackPropertyChanged;
        _playlistStore.LikedSongsPlaylist.Songs.CollectionChanged += OnLikedSongsChanged;
    }

    public AsyncRelayCommand<Song> DownloadSongCommand { get; }

    public AsyncRelayCommand<Song> OpenSongFolderCommand { get; }

    public RelayCommand<Song> ToggleLikeCommand { get; }

    /// <summary>Saved playlists, for the header "Add to playlist" menu.</summary>
    public ObservableCollection<Playlist> Playlists => _playlistStore.Playlists;

    /// <summary>The album header song (Title/Album = name, Artist, Artwork).</summary>
    public Song? Album
    {
        get => _album;
        private set
        {
            if (SetProperty(ref _album, value))
            {
                OnPropertyChanged(nameof(AlbumTitle));
                OnPropertyChanged(nameof(AlbumArtist));
                OnPropertyChanged(nameof(HasAlbum));
            }
        }
    }

    public string AlbumTitle => _album?.Album is { Length: > 0 } album
        ? album
        : _album?.Title ?? "Album";

    public string AlbumArtist => _album?.Artist ?? string.Empty;

    public bool HasAlbum => _album is not null;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public ObservableCollection<PlaylistTrackRow> TrackRows => _trackRows;

    public string TrackStatsText
    {
        get
        {
            if (_tracks.Count == 0)
            {
                return _isLoading ? "Loading…" : string.Empty;
            }

            string count = _tracks.Count == 1 ? "1 track" : $"{_tracks.Count} tracks";
            var total = TimeSpan.FromSeconds(_tracks.Sum(track => track.Duration.TotalSeconds));
            if (total <= TimeSpan.Zero)
            {
                return count;
            }

            string duration = total.TotalHours >= 1
                ? $"{(int)total.TotalHours}:{total.Minutes:D2}:{total.Seconds:D2}"
                : $"{total.Minutes}:{total.Seconds:D2}";
            return $"{count} · {duration}";
        }
    }

    public AsyncRelayCommand PlayAlbumCommand { get; }

    public AsyncRelayCommand ShuffleAlbumCommand { get; }

    public AsyncRelayCommand<Song> PlayTrackCommand { get; }

    public RelayCommand<Playlist> AddAlbumToPlaylistCommand { get; }

    public RelayCommand<Playlist> AddSongToPlaylistCommand { get; }

    /// <summary>Loads the album's cover and track list, replacing whatever was shown.</summary>
    public async Task ShowAlbumAsync(Song album)
    {
        ArgumentNullException.ThrowIfNull(album);

        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _loadCancellation.Token;

        Album = album;
        _tracks.Clear();
        _trackRows.Clear();
        IsLoading = true;
        OnPropertyChanged(nameof(TrackStatsText));
        PlayAlbumCommand.RaiseCanExecuteChanged();
        ShuffleAlbumCommand.RaiseCanExecuteChanged();

        _ = EnsureCoverAsync(album, cancellationToken);

        try
        {
            IEnumerable<Song> tracks = await _lastFmDataService.GetAlbumTracksAsync(
                album.Artist ?? string.Empty,
                album.Album is { Length: > 0 } name ? name : album.Title ?? string.Empty,
                cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            foreach (Song track in tracks)
            {
                _tracks.Add(track);
            }

            RebuildTrackRows();
            _ = ResolveAlbumYearAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _notifications.ShowError(exception.Message);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(TrackStatsText));
            PlayAlbumCommand.RaiseCanExecuteChanged();
            ShuffleAlbumCommand.RaiseCanExecuteChanged();
        }
    }

    // Resolve each track's year independently (iTunes track matches are fuzzy, so a
    // single-seed lookup can miss the whole album) — same approach as the playlist.
    private async Task ResolveAlbumYearAsync(CancellationToken cancellationToken)
    {
        var pending = _tracks
            .Where(track => track.Year is null
                && !string.IsNullOrWhiteSpace(track.Title)
                && !string.IsNullOrWhiteSpace(track.Artist))
            .ToList();

        foreach (Song track in pending)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                int? year = await _albumArtService.GetTrackYearAsync(
                    track.Artist,
                    track.Title,
                    cancellationToken);
                if (year is not null)
                {
                    track.Year ??= year;
                }
            }
            catch
            {
                // The year is decorative; failures are ignored.
            }
        }
    }

    private void AddAlbumToPlaylist(Playlist? playlist)
    {
        if (playlist is null || playlist.Id <= 0 || _tracks.Count == 0)
        {
            return;
        }

        foreach (Song track in _tracks)
        {
            _playlistStore.AddSongToPlaylist(playlist, CloneWithAlbumCover(track));
        }

        _notifications.ShowInfo($"Added “{AlbumTitle}” to “{playlist.Name}”.");
    }

    /// <summary>Records which track a per-row "more" menu is acting on.</summary>
    public void SetMenuSong(Song? song) => _menuSong = song;

    private void AddSongToPlaylist(Playlist? playlist)
    {
        if (playlist is null || playlist.Id <= 0 || _menuSong is null)
        {
            return;
        }

        _playlistStore.AddSongToPlaylist(playlist, CloneWithAlbumCover(_menuSong));
        _notifications.ShowInfo($"Added “{_menuSong.Title}” to “{playlist.Name}”.");
    }

    // Album tracks carry no per-track art, so stamp the album cover onto the queue
    // clone — otherwise the playlist shows gray boxes. ArtworkData is the only
    // artwork the DB persists, so set it for offline/reload survival.
    private Song CloneWithAlbumCover(Song track)
    {
        Song clone = track.CloneForQueue();
        Song? album = _album;

        if (album?.ArtworkData is { Length: > 0 } cover)
        {
            clone.ArtworkData = cover;
        }

        string? coverUrl = album is null
            ? null
            : !string.IsNullOrWhiteSpace(album.ArtworkUrl) ? album.ArtworkUrl : album.ThumbnailUrl;
        if (!string.IsNullOrWhiteSpace(coverUrl))
        {
            clone.ArtworkUrl = coverUrl;
            clone.ThumbnailUrl = coverUrl;
        }

        return clone;
    }

    // Album tracks carry only artist+title, so resolve a YouTube id first (as play
    // does), then run the same download pipeline the playlist uses. These tracks have
    // no DB row (Id == 0), so nothing is persisted beyond the file on disk.
    private async Task DownloadSongAsync(Song? song)
    {
        if (song is null || string.IsNullOrWhiteSpace(song.Title))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(song.FilePath) && File.Exists(song.FilePath))
        {
            song.CurrentDownloadState = DownloadState.Downloaded;
            return;
        }

        song.CurrentDownloadState = DownloadState.Downloading;
        song.DownloadProgress = 0;
        DownloadSongCommand.RaiseCanExecuteChanged();
        var progress = new Progress<double>(value =>
            song.DownloadProgress = Math.Clamp(value, 0, 1) * 100);

        try
        {
            string? videoId = song.VideoId;
            if (string.IsNullOrWhiteSpace(videoId))
            {
                videoId = await ResolveVideoIdAsync(song, CancellationToken.None);
                if (!string.IsNullOrWhiteSpace(videoId))
                {
                    song.VideoId = videoId;
                }
            }

            if (string.IsNullOrWhiteSpace(videoId))
            {
                song.CurrentDownloadState = DownloadState.NotDownloaded;
                song.DownloadProgress = 0;
                _notifications.ShowError($"Couldn't find “{song.Title}” to download.");
                return;
            }

            Song downloaded = await _youTubeMediaService.DownloadAudioAsync(
                videoId,
                progress,
                CancellationToken.None);

            song.FilePath = downloaded.FilePath;
            if (song.Duration <= TimeSpan.Zero && downloaded.Duration > TimeSpan.Zero)
            {
                song.Duration = downloaded.Duration;
            }

            if (song.Year is null && downloaded.Year is int year && year > 0)
            {
                song.Year = year;
            }

            if ((song.ArtworkData is null || song.ArtworkData.Length == 0)
                && downloaded.ArtworkData is { Length: > 0 })
            {
                song.ArtworkData = downloaded.ArtworkData;
            }

            song.CurrentDownloadState = DownloadState.Downloaded;
            song.DownloadProgress = 100;
            _notifications.ShowInfo($"Downloaded “{song.Title}” to your Music folder.");
        }
        catch
        {
            song.CurrentDownloadState = DownloadState.NotDownloaded;
            song.DownloadProgress = 0;
            throw;
        }
        finally
        {
            DownloadSongCommand.RaiseCanExecuteChanged();
            OpenSongFolderCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task<string?> ResolveVideoIdAsync(Song song, CancellationToken cancellationToken)
    {
        string query = $"{song.Artist} {song.Title}".Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var matches = await _youTubeMediaService.SearchAsync(
            query,
            1,
            searchSongs: true,
            cancellationToken);
        return matches.Count > 0 ? matches[0].VideoId : null;
    }

    private static Task ExecuteOpenSongFolderAsync(Song? song)
    {
        string? folder = song?.FilePath is { Length: > 0 } filePath
            ? Path.GetDirectoryName(filePath)
            : null;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException("The download folder no longer exists.");
        }

        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        return Task.CompletedTask;
    }

    private void ToggleLike(Song? song)
    {
        if (song is null)
        {
            return;
        }

        bool nowLiked = _playlistStore.ToggleFavorite(song);
        _notifications.ShowInfo(nowLiked ? "Added to Liked Songs." : "Removed from Liked Songs.");
        // The Liked Songs collection change refreshes each row's heart.
    }

    private void OnLikedSongsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshLikedStates();

    private void RefreshLikedStates()
    {
        foreach (PlaylistTrackRow row in _trackRows)
        {
            row.IsLiked = _playlistStore.IsFavorite(row.Song);
        }
    }

    private async Task EnsureCoverAsync(Song album, CancellationToken cancellationToken)
    {
        try
        {
            if (album.Artwork is not null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(album.ThumbnailUrl))
            {
                string? url = album.ArtworkUrl;
                if (string.IsNullOrWhiteSpace(url))
                {
                    url = await _albumArtService.GetAlbumArtworkUrlAsync(
                        album.Artist,
                        album.Album is { Length: > 0 } name ? name : album.Title,
                        cancellationToken);
                }

                if (!string.IsNullOrWhiteSpace(url))
                {
                    album.ThumbnailUrl = url;
                }
            }

            await album.LoadThumbnailAsync(cancellationToken: cancellationToken);
        }
        catch
        {
            // Cover is decorative; a placeholder shows if it can't be fetched.
        }
    }

    private void RebuildTrackRows()
    {
        _trackRows.Clear();
        int number = 1;
        Song? current = _playbackViewModel.CurrentSong;
        foreach (Song track in _tracks)
        {
            _trackRows.Add(new PlaylistTrackRow(number++, track, IsSameTrack(track, current))
            {
                IsLiked = _playlistStore.IsFavorite(track)
            });
        }
    }

    private static bool IsSameTrack(Song track, Song? current)
    {
        if (current is null)
        {
            return false;
        }

        if (ReferenceEquals(track, current))
        {
            return true;
        }

        return string.Equals(track.Title, current.Title, StringComparison.OrdinalIgnoreCase)
            && string.Equals(track.Artist, current.Artist, StringComparison.OrdinalIgnoreCase);
    }

    private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(PlaybackViewModel.CurrentSong))
        {
            return;
        }

        Song? current = _playbackViewModel.CurrentSong;
        foreach (PlaylistTrackRow row in _trackRows)
        {
            row.IsCurrentlyPlaying = IsSameTrack(row.Song, current);
        }
    }

    private async Task PlayTrackAsync(Song? song)
    {
        if (song is null || _tracks.Count == 0)
        {
            return;
        }

        var queue = new Playlist
        {
            Name = AlbumTitle,
            Songs = new ObservableCollection<Song>(_tracks)
        };
        _playbackViewModel.IsShuffleEnabled = false;
        _playbackViewModel.CurrentPlaylist = queue;
        await _playbackViewModel.PlayFromBeginning(song);
    }

    private async Task ShuffleAlbumAsync()
    {
        if (_tracks.Count == 0)
        {
            return;
        }

        await _playbackViewModel.PlayShuffledAsync(_tracks.ToList(), AlbumTitle);
    }

    public override void Dispose()
    {
        _playbackViewModel.PropertyChanged -= OnPlaybackPropertyChanged;
        _playlistStore.LikedSongsPlaylist.Songs.CollectionChanged -= OnLikedSongsChanged;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        base.Dispose();
    }
}
