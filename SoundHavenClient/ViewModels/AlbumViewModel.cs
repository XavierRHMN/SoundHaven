using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly ObservableCollection<Song> _tracks = new();
    private readonly ObservableCollection<PlaylistTrackRow> _trackRows = new();
    private Song? _album;
    private bool _isLoading;
    private CancellationTokenSource? _loadCancellation;

    public AlbumViewModel(
        PlaybackViewModel playbackViewModel,
        ILastFmDataService lastFmDataService,
        IAlbumArtService albumArtService,
        IUserNotificationService notifications,
        PlaylistStore playlistStore)
    {
        _playbackViewModel = playbackViewModel ?? throw new ArgumentNullException(nameof(playbackViewModel));
        _lastFmDataService = lastFmDataService ?? throw new ArgumentNullException(nameof(lastFmDataService));
        _albumArtService = albumArtService ?? throw new ArgumentNullException(nameof(albumArtService));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _playlistStore = playlistStore ?? throw new ArgumentNullException(nameof(playlistStore));

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

        _playbackViewModel.PropertyChanged += OnPlaybackPropertyChanged;
    }

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

        Song? album = _album;
        string? coverUrl = album is null
            ? null
            : !string.IsNullOrWhiteSpace(album.ArtworkUrl) ? album.ArtworkUrl : album.ThumbnailUrl;

        foreach (Song track in _tracks)
        {
            Song clone = track.CloneForQueue();

            // Album tracks carry no per-track art, so give each the album cover —
            // otherwise the playlist shows gray boxes. ArtworkData is the only
            // artwork the DB persists, so set it for offline/reload survival.
            if (album?.ArtworkData is { Length: > 0 } cover)
            {
                clone.ArtworkData = cover;
            }

            if (!string.IsNullOrWhiteSpace(coverUrl))
            {
                clone.ArtworkUrl = coverUrl;
                clone.ThumbnailUrl = coverUrl;
            }

            _playlistStore.AddSongToPlaylist(playlist, clone);
        }

        _notifications.ShowInfo($"Added “{AlbumTitle}” to “{playlist.Name}”.");
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
            _trackRows.Add(new PlaylistTrackRow(number++, track, IsSameTrack(track, current)));
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
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        base.Dispose();
    }
}
