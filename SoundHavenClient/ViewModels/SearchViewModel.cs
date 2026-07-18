using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using SoundHaven.Commands;
using SoundHaven.Models;
using SoundHaven.Services;
using SoundHaven.Stores;

namespace SoundHaven.ViewModels;

/// <summary>Search result filter pills, in display order.</summary>
public enum SearchSection
{
    Top,
    Albums,
    Songs,
    Artists,
    Videos
}

/// <summary>Numbered search result row for the TIDAL-style results table.</summary>
public sealed class SearchResultRow : ViewModelBase
{
    private bool _isCurrentlyPlaying;
    private bool _isLiked;

    public SearchResultRow(int number, Song song)
    {
        Number = number;
        Song = song ?? throw new ArgumentNullException(nameof(song));
    }

    public int Number { get; }

    public Song Song { get; }

    public bool IsCurrentlyPlaying
    {
        get => _isCurrentlyPlaying;
        set => SetProperty(ref _isCurrentlyPlaying, value);
    }

    public bool IsLiked
    {
        get => _isLiked;
        set => SetProperty(ref _isLiked, value);
    }
}

public sealed class SearchViewModel : ViewModelBase
{
    private readonly IYouTubeMediaService _youTubeMediaService;
    private readonly PlaybackViewModel _playbackViewModel;
    private readonly PlaylistStore _playlistStore;
    private readonly IAlbumArtService _albumArtService;
    private readonly IUserNotificationService _notifications;
    private readonly LikedAlbumsStore _likedAlbumsStore;
    private List<Song> _songResults = [];
    private List<Song> _videoResults = [];
    private List<Song> _albumResults = [];
    private List<Song> _artistResults = [];
    private CancellationTokenSource _searchCancellation = new();
    private CancellationTokenSource _lifetimeCancellation = new();
    private string _searchQuery = string.Empty;
    private bool _isLoading;
    private string _loadingMessage = string.Empty;
    private Song? _selectedSong;
    private Song? _menuSong;
    private bool _isScrollViewerHittestable = true;
    private SearchSection _activeSection = SearchSection.Top;
    private bool _showResults;

    public SearchViewModel(
        IYouTubeMediaService youTubeMediaService,
        PlaybackViewModel playbackViewModel,
        PlaylistStore playlistStore,
        IAlbumArtService albumArtService,
        IUserNotificationService notifications,
        LikedAlbumsStore likedAlbumsStore)
    {
        _youTubeMediaService = youTubeMediaService
            ?? throw new ArgumentNullException(nameof(youTubeMediaService));
        _playbackViewModel = playbackViewModel
            ?? throw new ArgumentNullException(nameof(playbackViewModel));
        _playlistStore = playlistStore
            ?? throw new ArgumentNullException(nameof(playlistStore));
        _likedAlbumsStore = likedAlbumsStore
            ?? throw new ArgumentNullException(nameof(likedAlbumsStore));
        _albumArtService = albumArtService
            ?? throw new ArgumentNullException(nameof(albumArtService));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

        SearchResults = [];
        SearchCommand = new AsyncRelayCommand(ExecuteSearchAsync, onException: ShowFailure);
        PlaySongCommand = new AsyncRelayCommand<Song>(
            ExecutePlaySongAsync,
            song => song is not null,
            ShowFailure);
        PlayNextCommand = new AsyncRelayCommand<Song>(
            ExecutePlayNextAsync,
            song => song is not null,
            ShowFailure);
        AddToQueueCommand = new AsyncRelayCommand<Song>(
            ExecuteAddToQueueAsync,
            song => song is not null,
            ShowFailure);
        AddToPlaylistCommand = new RelayCommand<Playlist>(
            ExecuteAddToPlaylist,
            playlist => playlist is not null && playlist.Id > 0 && _menuSong is not null);
        CreatePlaylistAndAddSongCommand = new AsyncRelayCommand(
            ExecuteCreatePlaylistAndAddSongAsync,
            () => _menuSong is not null,
            ShowFailure);
        DownloadSongCommand = new AsyncRelayCommand<Song>(
            ExecuteDownloadSongAsync,
            song => song is not null && song.CurrentDownloadState == DownloadState.NotDownloaded,
            ShowFailure);
        OpenFolderCommand = new AsyncRelayCommand<Song>(
            ExecuteOpenFolderAsync,
            song => !string.IsNullOrWhiteSpace(song?.FilePath),
            ShowFailure);
        RemoveDownloadCommand = new RelayCommand<Song>(
            RemoveDownload,
            song => song is not null && IsRemovableDownload(song));
        ClearSearchCommand = new RelayCommand(() => SearchQuery = string.Empty);
        SelectSectionCommand = new RelayCommand<SearchSection>(section => ActiveSection = section);
        SelectArtistCommand = new RelayCommand<Song>(SelectArtist, artist => artist is not null);
        ToggleLikeCommand = new RelayCommand<Song>(ToggleLike, song => song is not null);

        _playbackViewModel.PropertyChanged += OnPlaybackPropertyChanged;
        _playlistStore.LikedSongsPlaylist.Songs.CollectionChanged += OnLikedSongsChanged;
    }

    public RelayCommand<Song> ToggleLikeCommand { get; }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasQuery));
            if (string.IsNullOrWhiteSpace(value))
            {
                // Emptying the box returns to the regular Home content.
                ShowResults = false;
            }
        }
    }

    public bool HasQuery => !string.IsNullOrWhiteSpace(_searchQuery);

    /// <summary>True while the Home view should show search results instead of shelves.</summary>
    public bool ShowResults
    {
        get => _showResults;
        private set
        {
            if (SetProperty(ref _showResults, value))
            {
                OnPropertyChanged(nameof(ShowNoResults));
            }
        }
    }

    public bool ShowNoResults => ShowResults
        && !IsLoading
        && ResultRows.Count == 0
        && AlbumResults.Count == 0
        && ArtistResults.Count == 0;

    public ObservableCollection<Song> SearchResults { get; }

    public ObservableCollection<SearchResultRow> ResultRows { get; } = [];

    /// <summary>Album cards for the active section (top few on Top, all on Albums).</summary>
    public ObservableCollection<Song> AlbumResults { get; } = [];

    /// <summary>Artist cards for the active section (top few on Top, all on Artists).</summary>
    public ObservableCollection<Song> ArtistResults { get; } = [];

    public bool ShowSongsTable =>
        ActiveSection is SearchSection.Top or SearchSection.Songs or SearchSection.Videos
        && ResultRows.Count > 0;

    public bool ShowTopSongsHeader => IsTopSelected && ResultRows.Count > 0;

    public bool ShowAlbumsStrip =>
        ActiveSection is SearchSection.Top or SearchSection.Albums
        && AlbumResults.Count > 0;

    public bool ShowArtistsStrip =>
        ActiveSection is SearchSection.Top or SearchSection.Artists
        && ArtistResults.Count > 0;

    public ObservableCollection<Playlist> Playlists => _playlistStore.Playlists;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(ShowNoResults));
            }
        }
    }

    public string LoadingMessage
    {
        get => _loadingMessage;
        private set => SetProperty(ref _loadingMessage, value);
    }

    public Song? SelectedSong
    {
        get => _selectedSong;
        set => SetProperty(ref _selectedSong, value);
    }

    public bool IsScrollViewerHittestable
    {
        get => _isScrollViewerHittestable;
        private set => SetProperty(ref _isScrollViewerHittestable, value);
    }

    /// <summary>
    /// The active result filter pill. All result sets are fetched together, so
    /// switching just re-displays cached results — no new network request.
    /// </summary>
    public SearchSection ActiveSection
    {
        get => _activeSection;
        set
        {
            if (!SetProperty(ref _activeSection, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsTopSelected));
            OnPropertyChanged(nameof(IsAlbumsSelected));
            OnPropertyChanged(nameof(IsSongsSelected));
            OnPropertyChanged(nameof(IsArtistsSelected));
            OnPropertyChanged(nameof(IsVideosSelected));
            DisplayCurrentResults();
        }
    }

    public bool IsTopSelected => ActiveSection == SearchSection.Top;

    public bool IsAlbumsSelected => ActiveSection == SearchSection.Albums;

    public bool IsSongsSelected => ActiveSection == SearchSection.Songs;

    public bool IsArtistsSelected => ActiveSection == SearchSection.Artists;

    public bool IsVideosSelected => ActiveSection == SearchSection.Videos;

    public AsyncRelayCommand SearchCommand { get; }

    public RelayCommand ClearSearchCommand { get; }

    public RelayCommand<SearchSection> SelectSectionCommand { get; }

    /// <summary>Clicking an artist card searches that artist's songs.</summary>
    public RelayCommand<Song> SelectArtistCommand { get; }

    public AsyncRelayCommand<Song> PlaySongCommand { get; }

    public AsyncRelayCommand<Song> PlayNextCommand { get; }

    public AsyncRelayCommand<Song> AddToQueueCommand { get; }

    public RelayCommand<Playlist> AddToPlaylistCommand { get; }

    public AsyncRelayCommand CreatePlaylistAndAddSongCommand { get; }

    /// <summary>
    /// Opens the same create-playlist dialog as the sidebar's plus button; wired
    /// by the composition root (the dialog lives with the playlist page).
    /// </summary>
    public Func<Playlist, Task<bool>>? PromptPlaylistDetails { get; set; }

    public AsyncRelayCommand<Song> DownloadSongCommand { get; }

    public AsyncRelayCommand<Song> OpenFolderCommand { get; }

    public RelayCommand<Song> RemoveDownloadCommand { get; }

    public void SetMenuSong(Song? song)
    {
        _menuSong = song;
        AddToPlaylistCommand.RaiseCanExecuteChanged();
        CreatePlaylistAndAddSongCommand.RaiseCanExecuteChanged();
    }

    public override void Dispose()
    {
        _playbackViewModel.PropertyChanged -= OnPlaybackPropertyChanged;
        _playlistStore.LikedSongsPlaylist.Songs.CollectionChanged -= OnLikedSongsChanged;
        _searchCancellation.Cancel();
        _searchCancellation.Dispose();
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        base.Dispose();
    }

    private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaybackViewModel.CurrentSong))
        {
            UpdatePlayingHighlights();
        }
    }

    private void UpdatePlayingHighlights()
    {
        Song? current = _playbackViewModel.CurrentSong;
        foreach (SearchResultRow row in ResultRows)
        {
            row.IsCurrentlyPlaying = IsSameTrack(current, row.Song);
        }
    }

    private void OnLikedSongsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshLikedStates();

    private void RefreshLikedStates()
    {
        foreach (SearchResultRow row in ResultRows)
        {
            row.IsLiked = _playlistStore.IsFavorite(row.Song);
        }
    }

    private void ToggleLike(Song? song)
    {
        if (song is null)
        {
            return;
        }

        bool nowLiked = _playlistStore.ToggleFavorite(song);
        _notifications.ShowInfo(nowLiked ? "Added to Liked Songs." : "Removed from Liked Songs.");
    }

    // Queue playback clones songs, so reference equality alone is not enough.
    private static bool IsSameTrack(Song? current, Song song)
    {
        if (current is null)
        {
            return false;
        }

        if (ReferenceEquals(current, song))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(song.VideoId)
            && string.Equals(current.VideoId, song.VideoId, StringComparison.Ordinal);
    }

    private async Task ExecuteSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        var nextCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        CancellationTokenSource previousCancellation = Interlocked.Exchange(
            ref _searchCancellation,
            nextCancellation);
        previousCancellation.Cancel();
        previousCancellation.Dispose();

        ShowResults = true;
        IsLoading = true;
        LoadingMessage = "Searching...";
        SelectedSong = null;
        _songResults = [];
        _videoResults = [];
        _albumResults = [];
        _artistResults = [];
        SearchResults.Clear();
        ResultRows.Clear();
        AlbumResults.Clear();
        ArtistResults.Clear();
        try
        {
            // Search every section for the same query at once so switching
            // pills is instant.
            Task<List<Song>> songsTask = SafeSearchAsync(SearchQuery, true, nextCancellation.Token);
            Task<List<Song>> videosTask = SafeSearchAsync(SearchQuery, false, nextCancellation.Token);
            Task<List<Song>> albumsTask = SafeSearchAlbumsAsync(SearchQuery, nextCancellation.Token);
            Task<List<Song>> artistsTask = SafeSearchArtistsAsync(SearchQuery, nextCancellation.Token);
            await Task.WhenAll(songsTask, videosTask, albumsTask, artistsTask);
            nextCancellation.Token.ThrowIfCancellationRequested();

            _songResults = await songsTask;
            _videoResults = await videosTask;
            _albumResults = await albumsTask;
            _artistResults = await artistsTask;
            DisplayCurrentResults();
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, nextCancellation))
            {
                IsLoading = false;
                LoadingMessage = string.Empty;
                OnPropertyChanged(nameof(ShowNoResults));
            }
        }
    }

    private async Task<List<Song>> SafeSearchAlbumsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<YouTubeSearchResult> results = await _youTubeMediaService.SearchAlbumsAsync(
                query,
                12,
                cancellationToken);

            var albums = new List<Song>(results.Count);
            foreach (YouTubeSearchResult result in results)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var album = new Song
                {
                    Title = result.Title,
                    Album = result.Album ?? result.Title,
                    Artist = result.Author,
                    ThumbnailUrl = result.ThumbnailUrl,
                    ArtworkUrl = result.ThumbnailUrl,
                    Year = result.Year
                };
                album.IsAlbumLiked = _likedAlbumsStore.IsLiked(album);
                albums.Add(album);
                _ = LoadResultThumbnailAsync(album, cancellationToken);
            }

            return albums;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Search (albums) failed: {exception.Message}");
            return [];
        }
    }

    private async Task<List<Song>> SafeSearchArtistsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<YouTubeSearchResult> results = await _youTubeMediaService.SearchArtistsAsync(
                query,
                8,
                cancellationToken);

            var artists = new List<Song>(results.Count);
            foreach (YouTubeSearchResult result in results)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var artist = new Song
                {
                    Title = result.Title,
                    Artist = result.Author,
                    ThumbnailUrl = result.ThumbnailUrl
                };
                artists.Add(artist);
                _ = LoadResultThumbnailAsync(artist, cancellationToken);
            }

            return artists;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Search (artists) failed: {exception.Message}");
            return [];
        }
    }

    private void SelectArtist(Song? artist)
    {
        if (artist is null || string.IsNullOrWhiteSpace(artist.Title))
        {
            return;
        }

        SearchQuery = artist.Title;
        ActiveSection = SearchSection.Songs;
        SearchCommand.Execute(null);
    }

    private async Task<List<Song>> SafeSearchAsync(
        string query,
        bool searchSongs,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<YouTubeSearchResult> results = await _youTubeMediaService.SearchAsync(
                query,
                15,
                searchSongs,
                cancellationToken);

            var songs = new List<Song>(results.Count);
            foreach (YouTubeSearchResult result in results)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var song = new Song
                {
                    Title = result.Title,
                    Artist = result.Author,
                    Album = result.Album,
                    VideoId = result.VideoId,
                    ThumbnailUrl = result.ThumbnailUrl,
                    ChannelTitle = result.Author,
                    Duration = result.Duration ?? TimeSpan.Zero,
                    VideoDuration = FormatDuration(result.Duration),
                    Views = result.ViewCount > 0 ? FormatViewCount(result.ViewCount) : null,
                    Year = result.Year
                };
                songs.Add(song);
                _ = LoadResultThumbnailAsync(song, cancellationToken);
                _ = ResolveYearAsync(song, cancellationToken);
            }

            return songs;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A mode with no results (or a transient failure) just yields nothing.
            Debug.WriteLine($"Search ({(searchSongs ? "songs" : "videos")}) failed: {exception.Message}");
            return [];
        }
    }

    private void DisplayCurrentResults()
    {
        // The songs table: full list on Songs/Videos, a top-5 preview on Top.
        List<Song> tableSource = ActiveSection switch
        {
            SearchSection.Videos => _videoResults,
            SearchSection.Top => _songResults.Count > 5 ? _songResults.GetRange(0, 5) : _songResults,
            SearchSection.Songs => _songResults,
            _ => []
        };

        SearchResults.Clear();
        ResultRows.Clear();
        int number = 1;
        foreach (Song song in tableSource)
        {
            SearchResults.Add(song);
            ResultRows.Add(new SearchResultRow(number++, song)
            {
                IsLiked = _playlistStore.IsFavorite(song)
            });
        }

        AlbumResults.Clear();
        if (ActiveSection is SearchSection.Top or SearchSection.Albums)
        {
            int albumLimit = ActiveSection == SearchSection.Top ? 6 : _albumResults.Count;
            foreach (Song album in _albumResults)
            {
                if (AlbumResults.Count >= albumLimit)
                {
                    break;
                }

                AlbumResults.Add(album);
            }
        }

        ArtistResults.Clear();
        if (ActiveSection is SearchSection.Top or SearchSection.Artists)
        {
            int artistLimit = ActiveSection == SearchSection.Top ? 4 : _artistResults.Count;
            foreach (Song artist in _artistResults)
            {
                if (ArtistResults.Count >= artistLimit)
                {
                    break;
                }

                ArtistResults.Add(artist);
            }
        }

        UpdatePlayingHighlights();
        OnPropertyChanged(nameof(ShowSongsTable));
        OnPropertyChanged(nameof(ShowTopSongsHeader));
        OnPropertyChanged(nameof(ShowAlbumsStrip));
        OnPropertyChanged(nameof(ShowArtistsStrip));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    // YouTube search rarely provides a year; resolve it the same way the player
    // bar does, then assign on the UI thread (Avalonia doesn't marshal INPC).
    private async Task ResolveYearAsync(Song song, CancellationToken cancellationToken)
    {
        if (song.Year is not null || string.IsNullOrWhiteSpace(song.Title))
        {
            return;
        }

        try
        {
            int? year = await _albumArtService.GetTrackYearAsync(
                song.Artist,
                song.Title,
                cancellationToken);
            if (year is null)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => song.Year ??= year);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer search.
        }
        catch
        {
            // The year is decorative; never surface a failure.
        }
    }

    private static async Task LoadResultThumbnailAsync(Song song, CancellationToken cancellationToken)
    {
        try
        {
            await song.LoadThumbnailAsync(cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A newer search superseded this load.
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Thumbnail load failed for {song.VideoId}: {exception.Message}");
        }
    }

    private async Task ExecutePlaySongAsync(Song? song)
    {
        if (song is null)
        {
            return;
        }

        IsScrollViewerHittestable = false;
        try
        {
            if (song.NeedsHigherQualityArtwork())
            {
                await song.LoadThumbnailAsync(
                    forceReload: true,
                    cancellationToken: _lifetimeCancellation.Token);
            }

            var playlist = new Playlist
            {
                Name = "Up Next",
                Songs = new ObservableCollection<Song> { song }
            };
            _playbackViewModel.CurrentPlaylist = playlist;
            await _playbackViewModel.PlayFromBeginning(song);
        }
        finally
        {
            IsScrollViewerHittestable = true;
        }
    }

    private async Task ExecutePlayNextAsync(Song? song)
    {
        if (song is null)
        {
            return;
        }

        if (song.NeedsHigherQualityArtwork())
        {
            await song.LoadThumbnailAsync(
                forceReload: true,
                cancellationToken: _lifetimeCancellation.Token);
        }

        await _playbackViewModel.PlayNext(song);
        _notifications.ShowInfo($"Queued “{song.Title}” to play next.");
    }

    private async Task ExecuteAddToQueueAsync(Song? song)
    {
        if (song is null)
        {
            return;
        }

        if (song.NeedsHigherQualityArtwork())
        {
            await song.LoadThumbnailAsync(
                forceReload: true,
                cancellationToken: _lifetimeCancellation.Token);
        }

        await _playbackViewModel.AddToQueue(song);
        _notifications.ShowInfo($"Added “{song.Title}” to the queue.");
    }

    private void ExecuteAddToPlaylist(Playlist? playlist)
    {
        if (playlist is null || _menuSong is null)
        {
            return;
        }

        try
        {
            _playlistStore.AddSongToPlaylist(playlist, _menuSong);
            _notifications.ShowInfo($"Added “{_menuSong.Title}” to “{playlist.Name}”.");
        }
        catch (Exception exception)
        {
            ShowFailure(exception);
        }
    }

    private async Task ExecuteCreatePlaylistAndAddSongAsync()
    {
        Song? song = _menuSong;
        if (song is null)
        {
            return;
        }

        var playlist = new Playlist
        {
            Name = "New playlist",
            Songs = []
        };

        // The dialog lets the user name it (and cancel); creation only proceeds
        // on save. Without the wiring, fall back to silent creation.
        if (PromptPlaylistDetails is { } prompt && !await prompt(playlist))
        {
            return;
        }

        _playlistStore.AddPlaylist(playlist);
        _playlistStore.AddSongToPlaylist(playlist, song);
        _notifications.ShowInfo($"Created “{playlist.Name}” and added “{song.Title}”.");
    }

    private async Task ExecuteDownloadSongAsync(Song? song)
    {
        if (song?.VideoId is null)
        {
            return;
        }

        song.CurrentDownloadState = DownloadState.Downloading;
        song.DownloadProgress = 0;
        DownloadSongCommand.RaiseCanExecuteChanged();

        var progress = new Progress<double>(value =>
        {
            song.DownloadProgress = Math.Clamp(value, 0, 1) * 100;
        });

        try
        {
            Song downloadedSong = await _youTubeMediaService.DownloadAudioAsync(
                song.VideoId,
                progress,
                _lifetimeCancellation.Token);

            song.FilePath = downloadedSong.FilePath;
            song.Title = downloadedSong.Title;
            song.Artist = downloadedSong.Artist;
            song.Album = downloadedSong.Album;
            song.Duration = downloadedSong.Duration;
            song.Year = downloadedSong.Year;
            song.ArtworkData = downloadedSong.ArtworkData;
            if (!string.IsNullOrWhiteSpace(downloadedSong.ThumbnailUrl))
            {
                song.ThumbnailUrl = downloadedSong.ThumbnailUrl;
            }

            song.CurrentDownloadState = DownloadState.Downloaded;
            song.DownloadProgress = 100;
            _playlistStore.MarkDownloaded(song);
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
            OpenFolderCommand.RaiseCanExecuteChanged();
            RemoveDownloadCommand.RaiseCanExecuteChanged();
        }
    }

    // Clicking the downloaded check on a result deletes the local file; the song
    // goes back to streaming and leaves the Downloaded Songs playlist.
    private void RemoveDownload(Song? song)
    {
        if (song is null || !IsRemovableDownload(song))
        {
            return;
        }

        try
        {
            File.Delete(song.FilePath!);
        }
        catch
        {
            _notifications.ShowError($"Couldn't remove “{song.Title}” — the file may be in use.");
            return;
        }

        song.FilePath = null;
        song.CurrentDownloadState = DownloadState.NotDownloaded;
        song.DownloadProgress = 0;
        _playlistStore.MarkUndownloaded(song);
        DownloadSongCommand.RaiseCanExecuteChanged();
        RemoveDownloadCommand.RaiseCanExecuteChanged();
        _notifications.ShowInfo($"Removed download for “{song.Title}”.");
    }

    private static bool IsRemovableDownload(Song song) =>
        !string.IsNullOrWhiteSpace(song.VideoId)
        && !string.IsNullOrWhiteSpace(song.FilePath)
        && File.Exists(song.FilePath);

    private static Task ExecuteOpenFolderAsync(Song? song)
    {
        string? folder = song?.FilePath is { Length: > 0 } filePath
            ? Path.GetDirectoryName(filePath)
            : null;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException("The download folder no longer exists.");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
        return Task.CompletedTask;
    }

    private static string FormatDuration(TimeSpan? duration)
    {
        if (duration is null)
        {
            return "0:00";
        }

        return duration.Value.TotalHours >= 1
            ? duration.Value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.Value.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private static string FormatViewCount(long viewCount)
    {
        return viewCount switch
        {
            < 1_000 => $"{viewCount} views",
            < 1_000_000 => $"{viewCount / 1_000d:0.#}K views",
            < 1_000_000_000 => $"{viewCount / 1_000_000d:0.#}M views",
            _ => $"{viewCount / 1_000_000_000d:0.#}B views"
        };
    }

    private void ShowFailure(Exception exception)
    {
        _notifications.ShowError(exception.Message);
    }
}
