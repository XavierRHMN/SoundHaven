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
    private CancellationTokenSource _trackCancellation = new();
    private Song? _currentSong;
    private Playlist? _currentPlaylist;
    private bool _isShuffleEnabled;
    private bool _canPlaybackControl = true;
    private bool _isTransitioningTracks;

    public PlaybackViewModel(
        IAudioService audioService,
        RepeatViewModel repeatViewModel,
        ILastFmDataService lastFmDataService,
        ThemesViewModel themesViewModel,
        IUserNotificationService notifications)
    {
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _repeatViewModel = repeatViewModel ?? throw new ArgumentNullException(nameof(repeatViewModel));
        _lastFmDataService = lastFmDataService ?? throw new ArgumentNullException(nameof(lastFmDataService));
        _themesViewModel = themesViewModel ?? throw new ArgumentNullException(nameof(themesViewModel));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

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
        set => SetProperty(ref _currentPlaylist, value);
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
            if (CurrentPlaylist?.Songs.Count > 0)
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
        Song? nextSong = GetNextSong(wrap: true);
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

        Song? previous = CurrentPlaylist?.GetPreviousNextSong(CurrentSong, Direction.Previous);
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
            PlaybackSource source = SelectPlaybackSource(song, File.Exists);
            await _audioService.StartAsync(source, cancellationToken: replacementCancellation.Token);
            await EnsureArtworkForThemeAsync(song, replacementCancellation.Token);
            _ = ScrobbleCurrentSongAsync(song, replacementCancellation.Token);
            ApplyDynamicTheme(song);
        }
        finally
        {
            IsTransitioningTracks = false;
            CanPlaybackControl = true;
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

    public Task AddToUpNext(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        EnsureQueuePlaylist();

        // Always append a queue copy so duplicates are allowed.
        CurrentPlaylist!.Songs.Add(song.CloneForQueue());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Inserts a copy of <paramref name="song"/> at the front of Up Next
    /// (immediately after the currently playing track). Does not start playback.
    /// </summary>
    public Task PlayNext(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        EnsureQueuePlaylist();

        ObservableCollection<Song> songs = CurrentPlaylist!.Songs;
        Song queued = song.CloneForQueue();

        int insertIndex = 0;
        if (CurrentSong is not null)
        {
            int currentIndex = FindSongIndexByReference(songs, CurrentSong);
            if (currentIndex >= 0)
            {
                insertIndex = currentIndex + 1;
            }
        }

        insertIndex = Math.Clamp(insertIndex, 0, songs.Count);
        songs.Insert(insertIndex, queued);
        return Task.CompletedTask;
    }

    private void EnsureQueuePlaylist()
    {
        CurrentPlaylist ??= new Playlist
        {
            Name = "Up Next",
            Songs = new ObservableCollection<Song>()
        };
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

        int currentIndex = FindSongIndexByReference(songs, CurrentSong);
        if (currentIndex < 0)
        {
            return songs.ToList();
        }

        if (currentIndex >= songs.Count - 1)
        {
            return Array.Empty<Song>();
        }

        var upcoming = new List<Song>(songs.Count - currentIndex - 1);
        for (int i = currentIndex + 1; i < songs.Count; i++)
        {
            upcoming.Add(songs[i]);
        }

        return upcoming;
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
            int currentIndex = FindSongIndexByReference(songs, CurrentSong);
            if (currentIndex < 0)
            {
                return false;
            }

            baseIndex = currentIndex + 1;
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
            int currentIndex = FindSongIndexByReference(songs, CurrentSong);
            if (currentIndex >= 0)
            {
                baseIndex = currentIndex + 1;
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

        // Only allow removing songs after the currently playing track when it is in the queue.
        if (CurrentSong is not null)
        {
            int currentIndex = FindSongIndexByReference(songs, CurrentSong);
            if (currentIndex >= 0 && index <= currentIndex)
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

            int currentIndex = songs.IndexOf(CurrentSong);
            int nextIndex;
            do
            {
                nextIndex = Random.Shared.Next(songs.Count);
            }
            while (nextIndex == currentIndex);

            return songs[nextIndex];
        }

        int index = songs.IndexOf(CurrentSong);
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
                        Song? next = GetNextSong(wrap: true);
                        if (next is not null)
                        {
                            await PlayFromBeginning(next);
                        }

                        break;
                    }
                case RepeatMode.Off:
                    {
                        Song? next = GetNextSong(wrap: false);
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

    private async Task EnsureArtworkForThemeAsync(Song song, CancellationToken cancellationToken)
    {
        if (!_themesViewModel.IsDynamicThemeSelected)
        {
            return;
        }

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
        if (!_themesViewModel.IsDynamicThemeSelected)
        {
            return;
        }

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
