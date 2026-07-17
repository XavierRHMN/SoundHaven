using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using SoundHaven.Commands;
using SoundHaven.Helpers;
using SoundHaven.Models;
using SoundHaven.Services;
using SoundHaven.Stores;

namespace SoundHaven.ViewModels;

public sealed class HomeViewModel : ViewModelBase
{
    private readonly PlaylistStore _playlistStore;
    private readonly RecentPlaybackStore _recentPlaybackStore;
    private readonly PlaybackViewModel _playbackViewModel;
    private readonly PlaylistViewModel _playlistViewModel;
    private readonly NavigationService _navigation;
    private readonly IUserNotificationService _notifications;
    private readonly ILastFmDataService _lastFmDataService;
    private readonly IYouTubeMediaService _youTubeMediaService;
    private readonly IAlbumArtService _albumArtService;
    private readonly DislikedSongsStore _dislikedSongs;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private Song? _menuSong;
    private bool _showForYou = true;
    private bool _showAllRecentlyPlayed;
    private bool _isLoadingRecommendations;
    private bool _recommendationsFromTaste;
    private int _disposeState;
    private int _uploadArtworkPassRunning;

    public HomeViewModel(
        PlaylistStore playlistStore,
        RecentPlaybackStore recentPlaybackStore,
        PlaybackViewModel playbackViewModel,
        PlaylistViewModel playlistViewModel,
        NavigationService navigation,
        IUserNotificationService notifications,
        ILastFmDataService lastFmDataService,
        IYouTubeMediaService youTubeMediaService,
        IAlbumArtService albumArtService,
        SearchViewModel searchViewModel,
        DislikedSongsStore dislikedSongsStore)
    {
        Search = searchViewModel ?? throw new ArgumentNullException(nameof(searchViewModel));
        _playlistStore = playlistStore ?? throw new ArgumentNullException(nameof(playlistStore));
        _recentPlaybackStore = recentPlaybackStore
            ?? throw new ArgumentNullException(nameof(recentPlaybackStore));
        _playbackViewModel = playbackViewModel
            ?? throw new ArgumentNullException(nameof(playbackViewModel));
        _playlistViewModel = playlistViewModel
            ?? throw new ArgumentNullException(nameof(playlistViewModel));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _lastFmDataService = lastFmDataService
            ?? throw new ArgumentNullException(nameof(lastFmDataService));
        _youTubeMediaService = youTubeMediaService
            ?? throw new ArgumentNullException(nameof(youTubeMediaService));
        _albumArtService = albumArtService
            ?? throw new ArgumentNullException(nameof(albumArtService));
        _dislikedSongs = dislikedSongsStore
            ?? throw new ArgumentNullException(nameof(dislikedSongsStore));

        FeaturedPlaylists = [];
        FeaturedItems = [];
        UploadSongs = [];
        UploadSongsPreview = [];
        RecentlyPlayedPreview = [];
        RecommendedSongs = [];
        TopAlbums = [];

        _playlistStore.Playlists.CollectionChanged += OnPlaylistsChanged;
        _recentPlaybackStore.RecentSongs.CollectionChanged += OnRecentSongsChanged;
        _lastFmDataService.AuthenticationStateChanged += OnLastFmAuthenticationChanged;

        OpenPlaylistCommand = new RelayCommand<Playlist>(OpenPlaylist, playlist => playlist is not null);
        CreatePlaylistCommand = new AsyncRelayCommand(
            CreatePlaylistAsync,
            () => true,
            exception => _notifications.ShowError(exception.Message));
        DislikeSongCommand = new RelayCommand<Song>(ExecuteDislikeSong, song => song is not null);
        PlaySongCommand = new AsyncRelayCommand<Song>(
            PlaySongAsync,
            song => song is not null,
            exception => _notifications.ShowError(exception.Message));
        PlayNextCommand = new AsyncRelayCommand<Song>(
            PlayNextAsync,
            song => song is not null,
            exception => _notifications.ShowError(exception.Message));
        AddToPlaylistCommand = new RelayCommand<Playlist>(
            ExecuteAddToPlaylist,
            playlist => playlist is { Id: > 0 } && _menuSong is not null);
        CreatePlaylistAndAddSongCommand = new RelayCommand(
            ExecuteCreatePlaylistAndAddSong,
            () => _menuSong is not null);
        ShowForYouCommand = new RelayCommand(() => IsForYouSelected = true);
        ShowUploadsCommand = new RelayCommand(() => IsForYouSelected = false);
        ViewAllUploadsCommand = new RelayCommand(() => IsForYouSelected = false);
        ViewAllRecentlyPlayedCommand = new RelayCommand(() => ShowAllRecentlyPlayed = true);

        RefreshFeaturedPlaylists();
        RefreshUploads();
        RefreshRecentlyPlayedPreview();
        _ = LoadRecommendationsAsync();
        _ = LoadTopAlbumsAsync();
    }

    public ObservableCollection<Playlist> FeaturedPlaylists { get; }

    /// <summary>
    /// The featured grid's display items: the playlists plus a trailing
    /// <see cref="NewPlaylistCard"/> call-to-action while the grid isn't full.
    /// </summary>
    public ObservableCollection<object> FeaturedItems { get; }

    public ObservableCollection<Song> UploadSongs { get; }

    public ObservableCollection<Song> UploadSongsPreview { get; }

    public ObservableCollection<Song> RecentlyPlayedPreview { get; }

    public ObservableCollection<Song> RecommendedSongs { get; }

    public ObservableCollection<Song> TopAlbums { get; }

    /// <summary>Embedded TIDAL-style search shown from the Home top bar.</summary>
    public SearchViewModel Search { get; }

    public ObservableCollection<Playlist> Playlists => _playlistStore.Playlists;

    public string Greeting { get; } = DateTime.Now.Hour switch
    {
        < 12 => "Good morning",
        < 18 => "Good afternoon",
        _ => "Good evening"
    };

    public ObservableCollection<Song> RecentlyPlayedSongs => _recentPlaybackStore.RecentSongs;

    public bool IsForYouSelected
    {
        get => _showForYou;
        set
        {
            if (!SetProperty(ref _showForYou, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsUploadsSelected));
            OnPropertyChanged(nameof(ShowForYouContent));
            OnPropertyChanged(nameof(ShowUploadsContent));
        }
    }

    public bool IsUploadsSelected => !IsForYouSelected;

    public bool ShowForYouContent => IsForYouSelected;

    public bool ShowUploadsContent => IsUploadsSelected;

    public bool ShowAllRecentlyPlayed
    {
        get => _showAllRecentlyPlayed;
        set
        {
            if (SetProperty(ref _showAllRecentlyPlayed, value))
            {
                RefreshRecentlyPlayedPreview();
            }
        }
    }

    public bool IsLoadingRecommendations
    {
        get => _isLoadingRecommendations;
        private set
        {
            if (SetProperty(ref _isLoadingRecommendations, value))
            {
                OnPropertyChanged(nameof(ShowRecommendationsEmpty));
            }
        }
    }

    public bool HasFeaturedPlaylists => FeaturedPlaylists.Count > 0;

    public bool HasFeaturedItems => FeaturedItems.Count > 0;

    public bool HasUploads => UploadSongs.Count > 0;

    public bool HasRecentlyPlayed => RecentlyPlayedSongs.Count > 0;

    public bool HasRecommendations => RecommendedSongs.Count > 0;

    public string RecommendationsSubtitle => _recommendationsFromTaste
        ? "Similar to the music in your Last.fm library"
        : "Popular on YouTube Music right now";

    public bool HasTopAlbums => TopAlbums.Count > 0;

    public bool ShowRecommendationsEmpty =>
        !IsLoadingRecommendations && !HasRecommendations;

    public RelayCommand<Playlist> OpenPlaylistCommand { get; }

    public AsyncRelayCommand CreatePlaylistCommand { get; }

    public AsyncRelayCommand<Song> PlaySongCommand { get; }

    public AsyncRelayCommand<Song> PlayNextCommand { get; }

    public RelayCommand<Playlist> AddToPlaylistCommand { get; }

    public RelayCommand CreatePlaylistAndAddSongCommand { get; }

    public RelayCommand ShowForYouCommand { get; }

    public RelayCommand ShowUploadsCommand { get; }

    public RelayCommand ViewAllUploadsCommand { get; }

    public RelayCommand ViewAllRecentlyPlayedCommand { get; }

    public void SetMenuSong(Song song)
    {
        _menuSong = song ?? throw new ArgumentNullException(nameof(song));
        AddToPlaylistCommand.RaiseCanExecuteChanged();
        CreatePlaylistAndAddSongCommand.RaiseCanExecuteChanged();
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        _playlistStore.Playlists.CollectionChanged -= OnPlaylistsChanged;
        _recentPlaybackStore.RecentSongs.CollectionChanged -= OnRecentSongsChanged;
        _lastFmDataService.AuthenticationStateChanged -= OnLastFmAuthenticationChanged;
        foreach (Playlist playlist in _playlistStore.Playlists)
        {
            playlist.Songs.CollectionChanged -= OnPlaylistSongsChanged;
        }

        base.Dispose();
    }

    private void OpenPlaylist(Playlist? playlist)
    {
        if (playlist is null)
        {
            return;
        }

        _playlistViewModel.DisplayedPlaylist = playlist;
        _navigation.NavigateTo(_playlistViewModel);
    }

    private async Task PlaySongAsync(Song? song)
    {
        if (song is null)
        {
            return;
        }

        if (!await EnsurePlayableAsync(song))
        {
            return;
        }

        await _playbackViewModel.PlayFromBeginning(song);
    }

    private async Task PlayNextAsync(Song? song)
    {
        if (song is null)
        {
            return;
        }

        if (!await EnsurePlayableAsync(song))
        {
            return;
        }

        await _playbackViewModel.PlayNext(song);
        _notifications.ShowInfo($"Queued “{song.Title}” to play next.");
    }

    private async Task<bool> EnsurePlayableAsync(Song song)
    {
        if (!string.IsNullOrWhiteSpace(song.FilePath) && File.Exists(song.FilePath))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(song.VideoId))
        {
            return true;
        }

        try
        {
            string query = $"{song.Artist} {song.Title}".Trim();
            IReadOnlyList<YouTubeSearchResult> results = await _youTubeMediaService.SearchAsync(
                query,
                limit: 1,
                searchSongs: true,
                _lifetimeCancellation.Token);

            if (results.Count == 0)
            {
                _notifications.ShowError($"Could not find “{song.Title}” on YouTube Music.");
                return false;
            }

            YouTubeSearchResult match = results[0];
            song.VideoId = match.VideoId;
            if (!string.IsNullOrWhiteSpace(match.ThumbnailUrl))
            {
                song.ThumbnailUrl = match.ThumbnailUrl;
            }

            if (match.Duration is { } duration && duration > TimeSpan.Zero)
            {
                song.Duration = duration;
            }

            _ = song.LoadThumbnailAsync(cancellationToken: _lifetimeCancellation.Token);
            return true;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            _notifications.ShowError(exception.Message);
            return false;
        }
    }

    private async Task LoadRecommendationsAsync()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        IsLoadingRecommendations = true;
        try
        {
            // Prefer real discovery seeded from the user's Last.fm taste; the
            // generic YouTube Music feed is only a signed-out fallback.
            IReadOnlyList<Song> recommendations = await LoadTasteRecommendationsAsync();
            bool fromTaste = recommendations.Count > 0;
            if (!fromTaste)
            {
                recommendations = await LoadYouTubeRecommendationsAsync();
            }

            var display = recommendations
                .Where(song => !_dislikedSongs.IsDisliked(song))
                .Take(12)
                .ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _recommendationsFromTaste = fromTaste;
                RecommendedSongs.Clear();
                foreach (Song song in display)
                {
                    RecommendedSongs.Add(song);
                    _ = ResolveArtworkAsync(song);
                }

                OnPropertyChanged(nameof(HasRecommendations));
                OnPropertyChanged(nameof(ShowRecommendationsEmpty));
                OnPropertyChanged(nameof(RecommendationsSubtitle));
            });
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Disposing.
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Home recommendations failed: {exception.Message}");
        }
        finally
        {
            if (Volatile.Read(ref _disposeState) == 0)
            {
                IsLoadingRecommendations = false;
            }
        }
    }

    private async Task<IReadOnlyList<Song>> LoadYouTubeRecommendationsAsync()
    {
        try
        {
            IReadOnlyList<YouTubeSearchResult> results =
                await _youTubeMediaService.GetHomeRecommendationsAsync(
                    RecommendationFeed.MaxYtmSeeds,
                    _lifetimeCancellation.Token);

            return results.Select(MapYouTubeResult).ToList();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"YouTube Music home browse failed: {exception.Message}");
            return [];
        }
    }

    /// <summary>
    /// Real recommendations: a random handful of the user's Last.fm top tracks
    /// seed track.getSimilar lookups, so the shelf is "music like what you
    /// actually listen to" rather than a region-default chart. The user's own
    /// top tracks are excluded so it surfaces new music. Empty when signed out.
    /// </summary>
    private async Task<IReadOnlyList<Song>> LoadTasteRecommendationsAsync()
    {
        if (!_lastFmDataService.IsConfigured || !_lastFmDataService.IsAuthenticated)
        {
            return [];
        }

        List<Song> topTracks;
        try
        {
            topTracks = (await _lastFmDataService.GetTopTracksAsync(_lifetimeCancellation.Token))
                .Where(song => !string.IsNullOrWhiteSpace(song.Artist)
                    && !string.IsNullOrWhiteSpace(song.Title))
                .ToList();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Last.fm top tracks failed: {exception.Message}");
            return [];
        }

        // Seed from a random sample of the top tracks so the shelf varies.
        var seeds = topTracks
            .Take(15)
            .OrderBy(_ => Random.Shared.Next())
            .Take(5)
            .ToList();
        if (seeds.Count == 0)
        {
            return [];
        }

        // Recommend new music: exclude anything already in the user's top tracks.
        var seen = new HashSet<string>(
            topTracks.Select(RecommendationFeed.BuildDedupeKey),
            StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<Song>[] similarLists = await Task.WhenAll(
            seeds.Select(seed => LoadSimilarTracksAsync(seed.Artist!, seed.Title!)));

        var picks = new List<Song>();
        foreach (IReadOnlyList<Song> list in similarLists)
        {
            foreach (Song song in list)
            {
                string key = RecommendationFeed.BuildDedupeKey(song);
                if (!string.IsNullOrWhiteSpace(key) && seen.Add(key))
                {
                    picks.Add(song);
                }
            }
        }

        // Interleave so results aren't grouped by seed.
        return picks.OrderBy(_ => Random.Shared.Next()).ToList();
    }

    private async Task<IReadOnlyList<Song>> LoadSimilarTracksAsync(string artist, string title)
    {
        try
        {
            IEnumerable<Song> similar = await _lastFmDataService.GetSimilarTracksAsync(
                artist,
                title,
                limit: 8,
                _lifetimeCancellation.Token);
            return similar.ToList();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Last.fm similar tracks failed: {exception.Message}");
            return [];
        }
    }

    private static Song MapYouTubeResult(YouTubeSearchResult result)
    {
        return new Song
        {
            Title = result.Title,
            Artist = result.Author,
            Album = result.Album,
            VideoId = result.VideoId,
            ThumbnailUrl = result.ThumbnailUrl,
            ChannelTitle = result.Author,
            Duration = result.Duration ?? TimeSpan.Zero
        };
    }

    private async Task LoadTopAlbumsAsync()
    {
        if (!_lastFmDataService.IsConfigured || !_lastFmDataService.IsAuthenticated)
        {
            return;
        }

        try
        {
            IEnumerable<Song> albums =
                await _lastFmDataService.GetRecommendedAlbumsAsync(_lifetimeCancellation.Token);
            var top = albums.Take(6).ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                TopAlbums.Clear();
                foreach (Song album in top)
                {
                    TopAlbums.Add(album);
                    _ = ResolveArtworkAsync(album, isAlbum: true);
                }

                OnPropertyChanged(nameof(HasTopAlbums));
            });
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Disposing.
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Last.fm top albums failed: {exception.Message}");
        }
    }

    /// <summary>
    /// Best-effort artwork: prefer an existing YouTube thumb, then a trusted
    /// Last.fm URL, then a keyless Deezer/iTunes lookup for songs with neither.
    /// </summary>
    private async Task ResolveArtworkAsync(Song song, bool isAlbum = false)
    {
        try
        {
            if (!AlbumArtService.IsUsableArtworkUrl(song.ArtworkUrl))
            {
                song.ArtworkUrl = null;
            }

            if (string.IsNullOrWhiteSpace(song.ThumbnailUrl)
                && !string.IsNullOrWhiteSpace(song.ArtworkUrl))
            {
                song.ThumbnailUrl = song.ArtworkUrl;
            }

            if (string.IsNullOrWhiteSpace(song.ThumbnailUrl)
                && string.IsNullOrWhiteSpace(song.VideoId))
            {
                string? artworkUrl = isAlbum
                    ? await _albumArtService.GetAlbumArtworkUrlAsync(
                        song.Artist,
                        song.Album ?? song.Title,
                        _lifetimeCancellation.Token)
                    : await _albumArtService.GetTrackArtworkUrlAsync(
                        song.Artist,
                        song.Title,
                        _lifetimeCancellation.Token);

                if (string.IsNullOrWhiteSpace(artworkUrl))
                {
                    return;
                }

                song.ThumbnailUrl = artworkUrl;
            }

            await song.LoadThumbnailAsync(cancellationToken: _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Ignore.
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Artwork resolution failed: {exception.Message}");
        }
    }

    private async Task EnsureUploadArtworkAsync()
    {
        if (Interlocked.Exchange(ref _uploadArtworkPassRunning, 1) != 0)
        {
            return;
        }

        try
        {
            var missing = UploadSongs.Where(song => song.Artwork is null).Take(24).ToList();
            foreach (Song song in missing)
            {
                _lifetimeCancellation.Token.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(song.FilePath))
                {
                    byte[]? embedded = await Task.Run(
                        () => Mp3ToSongHelper.TryReadEmbeddedArtworkBytes(song.FilePath),
                        _lifetimeCancellation.Token);
                    if (embedded is { Length: > 0 })
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => song.ArtworkData = embedded);
                        continue;
                    }
                }

                await ResolveArtworkAsync(song);
            }
        }
        catch (OperationCanceledException)
        {
            // Disposing.
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Upload artwork pass failed: {exception.Message}");
        }
        finally
        {
            Volatile.Write(ref _uploadArtworkPassRunning, 0);
        }
    }

    /// <summary>The Last.fm service, exposed for the sign-in dialog.</summary>
    public ILastFmDataService LastFm => _lastFmDataService;

    public bool IsLastFmConnectVisible =>
        _lastFmDataService.IsConfigured && !_lastFmDataService.IsAuthenticated;

    public bool IsLastFmConnected => _lastFmDataService.IsAuthenticated;

    private void OnLastFmAuthenticationChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsLastFmConnectVisible));
        OnPropertyChanged(nameof(IsLastFmConnected));
        _ = LoadRecommendationsAsync();
        _ = LoadTopAlbumsAsync();
    }

    public RelayCommand<Song> DislikeSongCommand { get; }

    /// <summary>True when the song is currently shown in the Recommended shelf.</summary>
    public bool IsRecommendedSong(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        return RecommendedSongs.Contains(song);
    }

    /// <summary>
    /// "Not interested": persist the track locally and keep it out of every
    /// future Recommended shelf (dislikes on YouTube itself wouldn't help —
    /// the radio lookups that build the shelf are anonymous).
    /// </summary>
    private void ExecuteDislikeSong(Song? song)
    {
        if (song is null)
        {
            return;
        }

        try
        {
            _dislikedSongs.Dislike(song);
            for (int i = RecommendedSongs.Count - 1; i >= 0; i--)
            {
                if (_dislikedSongs.IsDisliked(RecommendedSongs[i]))
                {
                    RecommendedSongs.RemoveAt(i);
                }
            }

            OnPropertyChanged(nameof(HasRecommendations));
            OnPropertyChanged(nameof(ShowRecommendationsEmpty));
            _notifications.ShowInfo($"Got it — “{song.Title}” won't be recommended again.");
        }
        catch (Exception exception)
        {
            _notifications.ShowError(exception.Message);
        }
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
            RefreshUploads();
        }
        catch (Exception exception)
        {
            _notifications.ShowError(exception.Message);
        }
    }

    private void ExecuteCreatePlaylistAndAddSong()
    {
        if (_menuSong is null)
        {
            return;
        }

        try
        {
            var playlist = new Playlist
            {
                Name = "New playlist",
                Songs = []
            };
            _playlistStore.AddPlaylist(playlist);
            _playlistStore.AddSongToPlaylist(playlist, _menuSong);
            _notifications.ShowInfo($"Created “{playlist.Name}” and added “{_menuSong.Title}”.");
            RefreshFeaturedPlaylists();
            RefreshUploads();
        }
        catch (Exception exception)
        {
            _notifications.ShowError(exception.Message);
        }
    }

    private void OnPlaylistsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (Playlist playlist in e.OldItems.OfType<Playlist>())
            {
                playlist.Songs.CollectionChanged -= OnPlaylistSongsChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (Playlist playlist in e.NewItems.OfType<Playlist>())
            {
                playlist.Songs.CollectionChanged += OnPlaylistSongsChanged;
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (Playlist playlist in _playlistStore.Playlists)
            {
                playlist.Songs.CollectionChanged -= OnPlaylistSongsChanged;
                playlist.Songs.CollectionChanged += OnPlaylistSongsChanged;
            }
        }

        RefreshFeaturedPlaylists();
        RefreshUploads();
    }

    private void OnPlaylistSongsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshUploads();

    private void OnRecentSongsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshRecentlyPlayedPreview();

    private const int FeaturedGridCapacity = 6;
    private const int FeaturedGridColumns = 3;

    private void RefreshFeaturedPlaylists()
    {
        FeaturedPlaylists.Clear();
        foreach (Playlist playlist in _playlistStore.Playlists.Take(FeaturedGridCapacity))
        {
            FeaturedPlaylists.Add(playlist);
        }

        FeaturedItems.Clear();
        foreach (Playlist playlist in FeaturedPlaylists)
        {
            FeaturedItems.Add(playlist);
        }

        // Fill the rest of the current row with "New playlist" prompts so the
        // shelf never shows empty cells.
        int newPlaylistCards = NewPlaylistCardCount(FeaturedPlaylists.Count);
        for (int i = 0; i < newPlaylistCards; i++)
        {
            FeaturedItems.Add(new NewPlaylistCard());
        }

        OnPropertyChanged(nameof(HasFeaturedPlaylists));
        OnPropertyChanged(nameof(HasFeaturedItems));
    }

    private static int NewPlaylistCardCount(int playlistCount)
    {
        if (playlistCount >= FeaturedGridCapacity)
        {
            return 0;
        }

        if (playlistCount == 0)
        {
            return 1;
        }

        // Pad the partial last row up to a full row of three.
        int usedInLastRow = playlistCount % FeaturedGridColumns;
        return usedInLastRow == 0 ? 0 : FeaturedGridColumns - usedInLastRow;
    }

    private async Task CreatePlaylistAsync()
    {
        var playlist = new Playlist { Name = "New playlist" };
        if (!await _playlistViewModel.PromptPlaylistDetailsAsync(playlist, isCreating: true))
        {
            return;
        }

        _playlistStore.AddPlaylist(playlist);
        OpenPlaylist(playlist);
    }

    private void RefreshUploads()
    {
        UploadSongs.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Playlist playlist in _playlistStore.Playlists)
        {
            playlist.Songs.CollectionChanged -= OnPlaylistSongsChanged;
            playlist.Songs.CollectionChanged += OnPlaylistSongsChanged;

            foreach (Song song in playlist.Songs)
            {
                if (string.IsNullOrWhiteSpace(song.FilePath) || !File.Exists(song.FilePath))
                {
                    continue;
                }

                string key = song.FilePath;
                if (!seen.Add(key))
                {
                    continue;
                }

                UploadSongs.Add(song);
            }
        }

        UploadSongsPreview.Clear();
        foreach (Song song in UploadSongs.Take(6))
        {
            UploadSongsPreview.Add(song);
        }

        OnPropertyChanged(nameof(HasUploads));
        _ = EnsureUploadArtworkAsync();
    }

    private void RefreshRecentlyPlayedPreview()
    {
        RecentlyPlayedPreview.Clear();
        IEnumerable<Song> source = ShowAllRecentlyPlayed
            ? RecentlyPlayedSongs
            : RecentlyPlayedSongs.Take(6);

        foreach (Song song in source)
        {
            RecentlyPlayedPreview.Add(song);
        }

        OnPropertyChanged(nameof(HasRecentlyPlayed));
    }
}
