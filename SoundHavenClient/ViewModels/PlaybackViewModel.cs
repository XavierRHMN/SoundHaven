using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
            if (SetProperty(ref _currentSong, value))
            {
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
        SeekPositionReset?.Invoke(this, EventArgs.Empty);
        try
        {
            PlaybackSource source = SelectPlaybackSource(song, File.Exists);
            await _audioService.StartAsync(source, cancellationToken: replacementCancellation.Token);
            CurrentSong = song;
            _ = ScrobbleCurrentSongAsync(song, replacementCancellation.Token);
            ApplyDynamicTheme(song);
        }
        finally
        {
            IsTransitioningTracks = false;
            CanPlaybackControl = true;
        }
    }

    public Task AddToUpNext(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        CurrentPlaylist ??= new Playlist
        {
            Name = "Streaming from YouTube",
            Songs = new ObservableCollection<Song>()
        };

        if (!CurrentPlaylist.Songs.Contains(song))
        {
            CurrentPlaylist.Songs.Add(song);
            OnPropertyChanged(nameof(CurrentPlaylist));
        }

        return Task.CompletedTask;
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

    private void ApplyDynamicTheme(Song song)
    {
        if (!_themesViewModel.IsDynamicThemeSelected || song.Artwork is null)
        {
            return;
        }

        try
        {
            var dominantColor = DominantColorFinder.GetDominantColor(song.Artwork);
            _themesViewModel.ThemeColors[^1] = dominantColor;
            _themesViewModel.ChangeTheme(dominantColor);
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
