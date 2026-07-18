using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using SoundHaven.Commands;
using SoundHaven.Helpers;
using SoundHaven.Models;
using SoundHaven.Services;
using SoundHaven.Stores;

namespace SoundHaven.ViewModels;

public sealed class PlaybackViewModel : ViewModelBase
{
    public enum Direction
    {
        Previous = -1,
        Next = 1
    }

    private readonly IAudioService _audioService;
    private readonly ILastFmDataService _lastFmDataService;
    private readonly RepeatViewModel _repeatViewModel;
    private readonly ThemesViewModel _themesViewModel;
    private readonly IUserNotificationService _notifications;
    private readonly RecentPlaybackStore? _recentPlaybackStore;
    private readonly IYouTubeMediaService? _youTubeMediaService;
    private CancellationTokenSource _trackCancellation = new();
    private Song? _currentSong;
    private Playlist? _currentPlaylist;
    private Song? _queueAnchor;
    private bool _isShuffleEnabled;
    private bool _canPlaybackControl = true;
    private bool _isTransitioningTracks;

    public PlaybackViewModel(
        IAudioService audioService,
        RepeatViewModel repeatViewModel,
        ILastFmDataService lastFmDataService,
        ThemesViewModel themesViewModel,
        IUserNotificationService notifications,
        RecentPlaybackStore? recentPlaybackStore = null,
        IYouTubeMediaService? youTubeMediaService = null)
    {
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _repeatViewModel = repeatViewModel ?? throw new ArgumentNullException(nameof(repeatViewModel));
        _lastFmDataService = lastFmDataService ?? throw new ArgumentNullException(nameof(lastFmDataService));
        _themesViewModel = themesViewModel ?? throw new ArgumentNullException(nameof(themesViewModel));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _recentPlaybackStore = recentPlaybackStore;
        _youTubeMediaService = youTubeMediaService;

        _audioService.PlaybackStateChanged += OnPlaybackStateChanged;
        _audioService.PlaybackFailed += OnPlaybackFailed;
        _audioService.TrackEnded += OnTrackEnded;

        PlayCommand = new AsyncRelayCommand(Play, CanPlay, ShowCommandFailure);
        PauseCommand = new AsyncRelayCommand(Pause, CanPause, ShowCommandFailure);
        NextCommand = new AsyncRelayCommand(NextTrack, CanChangeTrack, ShowCommandFailure);
        PreviousCommand = new AsyncRelayCommand(PreviousTrack, CanChangeTrack, ShowCommandFailure);
    }

    public event EventHandler? SeekPositionReset;

    public bool IsShuffleEnabled
    {
        get => _isShuffleEnabled;
        set => SetProperty(ref _isShuffleEnabled, value);
    }

    public bool IsPlaying => _audioService.IsPlaying;

    public Song? CurrentSong
    {
        get => _currentSong;
        set
        {
            if (ReferenceEquals(_currentSong, value))
            {
                return;
            }

            if (_currentSong is not null)
            {
                _currentSong.PropertyChanged -= OnCurrentSongPropertyChanged;
            }

            if (SetProperty(ref _currentSong, value))
            {
                if (_currentSong is not null)
                {
                    _currentSong.PropertyChanged += OnCurrentSongPropertyChanged;
                    UpdateQueueAnchor(_currentSong);
                }

                OnPropertyChanged(nameof(CurrentSongExists));
                RaiseCommandStates();
            }
        }
    }

    public bool CurrentSongExists => CurrentSong is not null;

    public Playlist? CurrentPlaylist
    {
        get => _currentPlaylist;
        // The queue is always a detached copy of whatever playlist was handed in:
        // queue edits (reorder, remove, play-next inserts) must never mutate a
        // stored playlist's own song collection.
        set
        {
            if (SetProperty(ref _currentPlaylist, CreateQueueSnapshot(value)))
            {
                // New queue context: advancement re-anchors on the next queued play.
                _queueAnchor = null;
            }
        }
    }

    public bool CanPlaybackControl
    {
        get => _canPlaybackControl;
        set
        {
            if (SetProperty(ref _canPlaybackControl, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsTransitioningTracks
    {
        get => _isTransitioningTracks;
        private set
        {
            if (SetProperty(ref _isTransitioningTracks, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public AsyncRelayCommand PlayCommand { get; }

    public AsyncRelayCommand PauseCommand { get; }

    public AsyncRelayCommand NextCommand { get; }

    public AsyncRelayCommand PreviousCommand { get; }

    public async Task Play()
    {
        if (CurrentSong is null)
        {
            Song? queued = DequeueManualSong();
            if (queued is not null)
            {
                await PlayFromBeginning(queued);
            }
            else if (CurrentPlaylist?.Songs.Count > 0)
            {
                await PlayFromBeginning(CurrentPlaylist.Songs[0]);
            }

            return;
        }

        if (_audioService.IsPaused)
        {
            await _audioService.ResumeAsync();
        }
        else if (_audioService.IsStopped)
        {
            await PlayFromBeginning(CurrentSong);
        }
    }

    public async Task Pause()
    {
        if (_audioService.IsPlaying)
        {
            await _audioService.PauseAsync();
        }
    }

    public async Task NextTrack()
    {
        // Manually queued songs always play first. After that, only Repeat All
        // wraps at the end of the queue; otherwise Next is a no-op there.
        Song? nextSong = DequeueManualSong()
            ?? GetNextSong(wrap: _repeatViewModel.RepeatMode == RepeatMode.All);
        if (nextSong is not null)
        {
            await PlayFromBeginning(nextSong);
        }
    }

    public async Task PreviousTrack()
    {
        if (CurrentSong is null)
        {
            return;
        }

        if (_audioService.CurrentPosition >= TimeSpan.FromSeconds(5))
        {
            await _audioService.RestartAsync();
            SeekPositionReset?.Invoke(this, EventArgs.Empty);
            return;
        }

        Song? previous = GetPreviousSong();
        if (previous is not null && !ReferenceEquals(previous, CurrentSong))
        {
            await PlayFromBeginning(previous);
        }
        else
        {
            await _audioService.RestartAsync();
            SeekPositionReset?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task PlayFromBeginning(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);

        var replacementCancellation = new CancellationTokenSource();
        CancellationTokenSource previousCancellation = Interlocked.Exchange(
            ref _trackCancellation,
            replacementCancellation);
        previousCancellation.Cancel();
        previousCancellation.Dispose();

        IsTransitioningTracks = true;
        CanPlaybackControl = false;
        // Show the track in the playbar immediately (with loading state) while audio resolves.
        CurrentSong = song;
        SeekPositionReset?.Invoke(this, EventArgs.Empty);
        try
        {
            // Anything without a local file resolves to a YouTube stream first,
            // so Last.fm-sourced recommendations always end up playable.
            await EnsurePlayableAsync(song, replacementCancellation.Token);
            PlaybackSource source = SelectPlaybackSource(song, File.Exists);
            await _audioService.StartAsync(source, cancellationToken: replacementCancellation.Token);
            _recentPlaybackStore?.RecordPlay(song);
            _ = ScrobbleCurrentSongAsync(song, replacementCancellation.Token);
            // Dynamic theming must not delay playback start: fetch artwork and
            // recolour in the background.
            _ = ApplyThemeFromArtworkAsync(song, replacementCancellation.Token);
        }
        finally
        {
            IsTransitioningTracks = false;
            CanPlaybackControl = true;
        }
    }

    /// <summary>
    /// Ensures a track has a playable source. Local files and songs that already
    /// carry a YouTube id pass straight through; anything else (e.g. a Last.fm
    /// recommendation) is matched to a YouTube video by an artist/title search.
    /// </summary>
    private async Task EnsurePlayableAsync(Song song, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(song.FilePath) && File.Exists(song.FilePath))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(song.VideoId) || _youTubeMediaService is null)
        {
            return;
        }

        string query = $"{song.Artist} {song.Title}".Trim();
        if (query.Length == 0)
        {
            return;
        }

        IReadOnlyList<YouTubeSearchResult> results = await _youTubeMediaService.SearchAsync(
            query,
            YouTubeMatchHelper.ResolveSearchLimit,
            searchSongs: true,
            cancellationToken);
        // Pick by duration proximity when the track length is known — the raw top
        // result can be a totally different song (short interludes especially).
        YouTubeSearchResult? match = YouTubeMatchHelper.PickBestMatch(results, song);
        if (match is null)
        {
            return;
        }

        song.VideoId = match.VideoId;

        // Recommendations arrive with Last.fm's low-resolution cover (they carry no
        // video). Now that this resolved to a real YouTube track, adopt a high-res
        // thumbnail — the search result's cover, or maxresdefault as a fallback —
        // and reload the artwork so the large player cover is sharp. The reload is
        // fire-and-forget and keeps the current cover until the sharp one arrives,
        // so it neither blanks the art nor delays playback. Tracks that already
        // carry sharp artwork (e.g. album pages stamp their cover) keep it — the
        // resolved video's thumbnail may not even be the right cover.
        if (song.NeedsHigherQualityArtwork())
        {
            string highResThumbnail = !string.IsNullOrWhiteSpace(match.ThumbnailUrl)
                ? match.ThumbnailUrl
                : YouTubeThumbnailHelper.GetVideoThumbnailUrl(match.VideoId);
            if (!string.Equals(song.ThumbnailUrl, highResThumbnail, StringComparison.Ordinal))
            {
                song.ThumbnailUrl = highResThumbnail;
                _ = song.LoadThumbnailAsync(forceReload: true, cancellationToken: cancellationToken);
            }
        }

        if (match.Duration is { } duration && duration > TimeSpan.Zero)
        {
            song.Duration = duration;
        }
    }

    /// <summary>
    /// Plays a random song from <paramref name="songs"/> and queues the remaining
    /// tracks in a random order. Does not mutate the source collection.
    /// </summary>
    public async Task PlayShuffledAsync(IReadOnlyList<Song> songs, string? playlistName = null)
    {
        ArgumentNullException.ThrowIfNull(songs);
        if (songs.Count == 0)
        {
            throw new ArgumentException("At least one song is required.", nameof(songs));
        }

        var shuffled = songs.ToList();
        ShuffleInPlace(shuffled);

        // Sequential playback through the shuffled queue (not continuous random pick).
        IsShuffleEnabled = false;
        CurrentPlaylist = new Playlist
        {
            Name = string.IsNullOrWhiteSpace(playlistName) ? "Shuffle" : playlistName,
            Songs = new ObservableCollection<Song>(shuffled)
        };

        await PlayFromBeginning(shuffled[0]);
    }

    private static void ShuffleInPlace(IList<Song> songs)
    {
        for (int i = songs.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (songs[i], songs[j]) = (songs[j], songs[i]);
        }
    }

    /// <summary>
    /// Manually queued tracks ("Play next" / "Add to queue"). They play before the
    /// playlist continues and are consumed as they play; Up Next itself stays
    /// strictly the playlist's own continuation.
    /// </summary>
    public ObservableCollection<Song> ManualQueue { get; } = new();

    /// <summary>Appends a copy of <paramref name="song"/> to the end of the manual
    /// queue. Duplicates are allowed.</summary>
    public Task AddToQueue(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        ManualQueue.Add(song.CloneForQueue());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Inserts a copy of <paramref name="song"/> at the head of the manual queue,
    /// so it plays immediately after the current track. Does not start playback.
    /// </summary>
    public Task PlayNext(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        ManualQueue.Insert(0, song.CloneForQueue());
        return Task.CompletedTask;
    }

    /// <summary>Plays a manually queued track now, consuming its queue entry.</summary>
    public Task PlayFromQueueAsync(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        ManualQueue.Remove(song);
        return PlayFromBeginning(song);
    }

    /// <summary>Removes a manually queued track without playing it.</summary>
    public bool RemoveFromQueue(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        return ManualQueue.Remove(song);
    }

    // Manually queued songs take priority over playlist advancement; they never
    // join the playlist, so the queue anchor keeps the playlist position and
    // Up Next resumes where it left off once the manual queue drains.
    private Song? DequeueManualSong()
    {
        if (ManualQueue.Count == 0)
        {
            return null;
        }

        Song next = ManualQueue[0];
        ManualQueue.RemoveAt(0);
        return next;
    }

    private static Playlist? CreateQueueSnapshot(Playlist? source)
    {
        if (source is null)
        {
            return null;
        }

        return new Playlist
        {
            Name = source.Name,
            Songs = new ObservableCollection<Song>(source.Songs)
        };
    }

    /// <summary>
    /// Remembers the queue position advancement is measured from. Detached
    /// tracks (history replays, one-off plays) never join the queue, so the
    /// anchor stays on the last queued track and the queue resumes from there.
    /// </summary>
    private void UpdateQueueAnchor(Song song)
    {
        if (CurrentPlaylist?.Songs is { } songs
            && FindSongIndexByReference(songs, song) >= 0)
        {
            _queueAnchor = song;
        }
    }

    /// <summary>
    /// Queue index that Up Next, advancement, and queue edits are relative to:
    /// the current track when it is queued, otherwise the anchor.
    /// </summary>
    private int GetQueueReferenceIndex(ObservableCollection<Song> songs)
    {
        if (CurrentSong is not null)
        {
            int currentIndex = FindSongIndexByReference(songs, CurrentSong);
            if (currentIndex >= 0)
            {
                return currentIndex;
            }
        }

        if (_queueAnchor is not null)
        {
            int anchorIndex = FindSongIndexByReference(songs, _queueAnchor);
            if (anchorIndex >= 0)
            {
                return anchorIndex;
            }
        }

        return -1;
    }

    private Song? GetPreviousSong()
    {
        if (CurrentSong is null || CurrentPlaylist?.Songs is not { Count: > 0 } songs)
        {
            return null;
        }

        // From a detached track, Previous returns to the queue track it interrupted.
        if (FindSongIndexByReference(songs, CurrentSong) < 0)
        {
            return _queueAnchor is not null
                && FindSongIndexByReference(songs, _queueAnchor) >= 0
                    ? _queueAnchor
                    : null;
        }

        return CurrentPlaylist.GetPreviousNextSong(CurrentSong, Direction.Previous);
    }

    /// <summary>
    /// Songs queued after the currently playing track.
    /// </summary>
    public IReadOnlyList<Song> GetUpcomingSongs()
    {
        if (CurrentPlaylist?.Songs is not { Count: > 0 } songs)
        {
            return Array.Empty<Song>();
        }

        if (CurrentSong is null)
        {
            return songs.ToList();
        }

        int referenceIndex = GetQueueReferenceIndex(songs);
        if (referenceIndex < 0)
        {
            return songs.ToList();
        }

        if (referenceIndex >= songs.Count - 1)
        {
            return Array.Empty<Song>();
        }

        var upcoming = new List<Song>(songs.Count - referenceIndex - 1);
        for (int i = referenceIndex + 1; i < songs.Count; i++)
        {
            upcoming.Add(songs[i]);
        }

        return upcoming;
    }

    /// <summary>
    /// Songs queued before the currently playing track — its history within the
    /// current queue. Empty for the first track and for a one-off play (a
    /// single-song queue), which is what wipes history on a YouTube-search play.
    /// </summary>
    public IReadOnlyList<Song> GetPreviousSongs()
    {
        if (CurrentPlaylist?.Songs is not { Count: > 0 } songs || CurrentSong is null)
        {
            return Array.Empty<Song>();
        }

        int referenceIndex = GetQueueReferenceIndex(songs);
        if (referenceIndex <= 0)
        {
            return Array.Empty<Song>();
        }

        var previous = new List<Song>(referenceIndex);
        for (int i = 0; i < referenceIndex; i++)
        {
            previous.Add(songs[i]);
        }

        return previous;
    }

    /// <summary>
    /// Reorders an upcoming queue entry. Indices are relative to <see cref="GetUpcomingSongs"/>.
    /// </summary>
    public bool MoveUpNext(int fromUpNextIndex, int toUpNextIndex)
    {
        if (CurrentPlaylist?.Songs is not { } songs || songs.Count == 0)
        {
            return false;
        }

        int baseIndex = 0;
        if (CurrentSong is not null)
        {
            int referenceIndex = GetQueueReferenceIndex(songs);
            if (referenceIndex < 0)
            {
                return false;
            }

            baseIndex = referenceIndex + 1;
        }

        int from = baseIndex + fromUpNextIndex;
        int to = baseIndex + toUpNextIndex;
        if (from < baseIndex || from >= songs.Count || to < baseIndex || to >= songs.Count)
        {
            return false;
        }

        if (from == to)
        {
            return true;
        }

        songs.Move(from, to);
        return true;
    }

    /// <summary>
    /// Removes a queued song from Up Next by upcoming-queue index (0 = next to play).
    /// </summary>
    public bool RemoveFromUpNextAt(int upNextIndex)
    {
        if (CurrentPlaylist?.Songs is not { } songs || songs.Count == 0)
        {
            return false;
        }

        int baseIndex = 0;
        if (CurrentSong is not null)
        {
            int referenceIndex = GetQueueReferenceIndex(songs);
            if (referenceIndex >= 0)
            {
                baseIndex = referenceIndex + 1;
            }
        }

        int playlistIndex = baseIndex + upNextIndex;
        if (playlistIndex < baseIndex || playlistIndex >= songs.Count)
        {
            return false;
        }

        songs.RemoveAt(playlistIndex);
        return true;
    }

    /// <summary>
    /// Removes a queued song from Up Next. Does not affect the currently playing track.
    /// </summary>
    public bool RemoveFromUpNext(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        if (CurrentPlaylist?.Songs is not { } songs || songs.Count == 0)
        {
            return false;
        }

        if (CurrentSong is not null && ReferenceEquals(CurrentSong, song))
        {
            return false;
        }

        int index = FindSongIndexByReference(songs, song);
        if (index < 0)
        {
            return false;
        }

        // Only allow removing songs after the queue's reference position.
        if (CurrentSong is not null)
        {
            int referenceIndex = GetQueueReferenceIndex(songs);
            if (referenceIndex >= 0 && index <= referenceIndex)
            {
                return false;
            }
        }

        songs.RemoveAt(index);
        return true;
    }

    private static int FindSongIndexByReference(ObservableCollection<Song> songs, Song song)
    {
        for (int i = 0; i < songs.Count; i++)
        {
            if (ReferenceEquals(songs[i], song))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindSongIndex(ObservableCollection<Song> songs, Song song)
    {
        for (int i = 0; i < songs.Count; i++)
        {
            Song candidate = songs[i];
            if (ReferenceEquals(candidate, song))
            {
                return i;
            }

            if (!string.IsNullOrWhiteSpace(song.VideoId)
                && string.Equals(candidate.VideoId, song.VideoId, StringComparison.Ordinal))
            {
                return i;
            }

            if (!string.IsNullOrWhiteSpace(song.FilePath)
                && string.Equals(candidate.FilePath, song.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public static PlaybackSource SelectPlaybackSource(
        Song song,
        Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(song);
        fileExists ??= File.Exists;

        if (!string.IsNullOrWhiteSpace(song.FilePath) && fileExists(song.FilePath))
        {
            return new PlaybackSource.LocalFile(song.FilePath);
        }

        if (!string.IsNullOrWhiteSpace(song.VideoId))
        {
            return new PlaybackSource.YouTube(song.VideoId);
        }

        if (!string.IsNullOrWhiteSpace(song.FilePath))
        {
            return new PlaybackSource.LocalFile(song.FilePath);
        }

        throw new InvalidOperationException("This track has no playable file or YouTube source.");
    }

    public override void Dispose()
    {
        if (_currentSong is not null)
        {
            _currentSong.PropertyChanged -= OnCurrentSongPropertyChanged;
        }

        _trackCancellation.Cancel();
        _trackCancellation.Dispose();
        _audioService.PlaybackStateChanged -= OnPlaybackStateChanged;
        _audioService.PlaybackFailed -= OnPlaybackFailed;
        _audioService.TrackEnded -= OnTrackEnded;
        base.Dispose();
    }

    private bool CanPlay()
    {
        return CurrentSongExists
            && !_audioService.IsPlaying
            && CanPlaybackControl
            && !IsTransitioningTracks;
    }

    private bool CanPause()
    {
        return CurrentSongExists
            && _audioService.IsPlaying
            && !IsTransitioningTracks;
    }

    private bool CanChangeTrack()
    {
        return CurrentSongExists && CanPlaybackControl && !IsTransitioningTracks;
    }

    private Song? GetNextSong(bool wrap)
    {
        if (CurrentSong is null || CurrentPlaylist?.Songs.Count is null or 0)
        {
            return null;
        }

        ObservableCollection<Song> songs = CurrentPlaylist.Songs;
        if (IsShuffleEnabled)
        {
            if (songs.Count == 1)
            {
                return wrap ? songs[0] : null;
            }

            int currentIndex = GetQueueReferenceIndex(songs);
            int nextIndex;
            do
            {
                nextIndex = Random.Shared.Next(songs.Count);
            }
            while (nextIndex == currentIndex);

            return songs[nextIndex];
        }

        int index = GetQueueReferenceIndex(songs);
        if (index < 0)
        {
            return songs[0];
        }

        int next = index + 1;
        if (next < songs.Count)
        {
            return songs[next];
        }

        return wrap ? songs[0] : null;
    }

    private async Task HandleTrackEndedAsync()
    {
        try
        {
            switch (_repeatViewModel.RepeatMode)
            {
                case RepeatMode.One when CurrentSong is not null:
                    await PlayFromBeginning(CurrentSong);
                    break;
                case RepeatMode.All:
                    {
                        Song? next = DequeueManualSong() ?? GetNextSong(wrap: true);
                        if (next is not null)
                        {
                            await PlayFromBeginning(next);
                        }

                        break;
                    }
                case RepeatMode.Off:
                    {
                        Song? next = DequeueManualSong() ?? GetNextSong(wrap: false);
                        if (next is not null)
                        {
                            await PlayFromBeginning(next);
                        }

                        break;
                    }
            }
        }
        catch (OperationCanceledException)
        {
            // A manual track change superseded automatic progression.
        }
        catch (Exception exception)
        {
            _notifications.ShowError($"Could not continue playback: {exception.Message}");
        }
    }

    private async Task ScrobbleCurrentSongAsync(Song song, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(song.Title) || string.IsNullOrWhiteSpace(song.Artist))
        {
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _lastFmDataService.ScrobbleTrackAsync(
                song.Title,
                song.Artist,
                song.Album ?? string.Empty,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The track changed before scrobbling completed.
        }
        catch
        {
            // Last.fm is optional and should never interrupt playback.
        }
    }

    private async Task ApplyThemeFromArtworkAsync(Song song, CancellationToken cancellationToken)
    {
        try
        {
            await EnsureArtworkForThemeAsync(song, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyDynamicTheme(song);
        }
        else
        {
            Dispatcher.UIThread.Post(() => ApplyDynamicTheme(song));
        }
    }

    private static async Task EnsureArtworkForThemeAsync(Song song, CancellationToken cancellationToken)
    {
        if (!song.NeedsHigherQualityArtwork()
            && song.ArtworkData is { Length: > 0 })
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(song.ThumbnailUrl) && string.IsNullOrWhiteSpace(song.VideoId))
        {
            return;
        }

        try
        {
            await song.LoadThumbnailAsync(
                forceReload: song.NeedsHigherQualityArtwork(),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Artwork is optional for playback; theme will stay as-is.
        }
    }

    private void OnCurrentSongPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not Song song
            || !ReferenceEquals(song, CurrentSong)
            || e.PropertyName is not (nameof(Song.ArtworkData) or nameof(Song.Artwork)))
        {
            return;
        }

        void Apply() => ApplyDynamicTheme(song);
        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    private void ApplyDynamicTheme(Song song)
    {
        try
        {
            Color dominantColor;
            if (song.ArtworkData is { Length: > 0 })
            {
                dominantColor = DominantColorFinder.GetDominantColor(song.ArtworkData);
            }
            else if (song.Artwork is not null)
            {
                dominantColor = DominantColorFinder.GetDominantColor(song.Artwork);
            }
            else
            {
                return;
            }

            _themesViewModel.ApplyDynamicColor(dominantColor);
        }
        catch
        {
            // Dynamic theming is decorative and must not interrupt playback.
        }
    }

    private void OnTrackEnded(object? sender, EventArgs eventArgs)
    {
        _ = HandleTrackEndedAsync();
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs eventArgs)
    {
        void Update()
        {
            OnPropertyChanged(nameof(IsPlaying));
            RaiseCommandStates();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Update();
        }
        else
        {
            Dispatcher.UIThread.Post(Update);
        }
    }

    private void OnPlaybackFailed(object? sender, PlaybackFailedEventArgs eventArgs)
    {
        _notifications.ShowError($"{eventArgs.UserMessage} {eventArgs.Exception.Message}");
    }

    private void RaiseCommandStates()
    {
        PlayCommand.RaiseCanExecuteChanged();
        PauseCommand.RaiseCanExecuteChanged();
        NextCommand.RaiseCanExecuteChanged();
        PreviousCommand.RaiseCanExecuteChanged();
    }

    private void ShowCommandFailure(Exception exception)
    {
        _notifications.ShowError(exception.Message);
    }
}
