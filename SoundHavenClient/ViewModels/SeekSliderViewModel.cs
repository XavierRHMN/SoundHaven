using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using SoundHaven.Services;

namespace SoundHaven.ViewModels;

public sealed class SeekSliderViewModel : ViewModelBase
{
    private readonly IAudioService _audioService;
    private readonly PlaybackViewModel _playbackViewModel;
    private readonly IUserNotificationService _notifications;
    private readonly DispatcherTimer _debounceTimer;
    private CancellationTokenSource _seekCancellation = new();
    private double _seekPosition;
    private bool _isUpdatingFromPlayer;
    private bool _isUserSeeking;

    public SeekSliderViewModel(
        IAudioService audioService,
        PlaybackViewModel playbackViewModel,
        IUserNotificationService notifications)
    {
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _playbackViewModel = playbackViewModel
            ?? throw new ArgumentNullException(nameof(playbackViewModel));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(175)
        };
        _debounceTimer.Tick += OnDebounceElapsed;
        _audioService.PropertyChanged += OnAudioServicePropertyChanged;
        _playbackViewModel.PropertyChanged += OnPlaybackViewModelPropertyChanged;
        _playbackViewModel.SeekPositionReset += OnSeekPositionReset;
    }

    public double MaximumSeekValue => Math.Max(0, _audioService.TotalDuration.TotalSeconds);

    public double SeekPosition
    {
        get => _seekPosition;
        set
        {
            double clamped = Math.Clamp(value, 0, MaximumSeekValue);
            if (!SetProperty(ref _seekPosition, clamped) || _isUpdatingFromPlayer)
            {
                return;
            }

            if (!CanInteractSeekSlider)
            {
                return;
            }

            _isUserSeeking = true;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    public bool CanInteractSeekSlider =>
        _playbackViewModel.CurrentSong is not null
        && !_playbackViewModel.IsTransitioningTracks
        && !_audioService.IsSeekBuffering
        && MaximumSeekValue > 0;

    public override void Dispose()
    {
        _audioService.PropertyChanged -= OnAudioServicePropertyChanged;
        _playbackViewModel.PropertyChanged -= OnPlaybackViewModelPropertyChanged;
        _playbackViewModel.SeekPositionReset -= OnSeekPositionReset;
        _debounceTimer.Stop();
        _debounceTimer.Tick -= OnDebounceElapsed;
        _seekCancellation.Cancel();
        _seekCancellation.Dispose();
        base.Dispose();
    }

    private void OnDebounceElapsed(object? sender, EventArgs eventArgs)
    {
        _debounceTimer.Stop();
        _ = CommitSeekAsync();
    }

    private async Task CommitSeekAsync()
    {
        var replacement = new CancellationTokenSource();
        CancellationTokenSource previous = Interlocked.Exchange(
            ref _seekCancellation,
            replacement);
        previous.Cancel();
        previous.Dispose();

        try
        {
            await _audioService.SeekAsync(
                TimeSpan.FromSeconds(SeekPosition),
                replacement.Token);
        }
        catch (OperationCanceledException)
        {
            // A newer slider position superseded this seek.
        }
        catch (Exception exception)
        {
            _notifications.ShowError($"Could not seek: {exception.Message}");
        }
        finally
        {
            if (ReferenceEquals(_seekCancellation, replacement))
            {
                _isUserSeeking = false;
            }
        }
    }

    private void OnSeekPositionReset(object? sender, EventArgs eventArgs)
    {
        UpdateFromPlayer(0);
    }

    private void OnPlaybackViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(PlaybackViewModel.CurrentSong)
            or nameof(PlaybackViewModel.IsTransitioningTracks))
        {
            OnPropertyChanged(nameof(CanInteractSeekSlider));
            OnPropertyChanged(nameof(MaximumSeekValue));
        }
    }

    private void OnAudioServicePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        switch (eventArgs.PropertyName)
        {
            case nameof(IAudioService.CurrentPosition):
                if (!_isUserSeeking && !_playbackViewModel.IsTransitioningTracks)
                {
                    UpdateFromPlayer(_audioService.CurrentPosition.TotalSeconds);
                }

                break;
            case nameof(IAudioService.TotalDuration):
                OnPropertyChanged(nameof(MaximumSeekValue));
                OnPropertyChanged(nameof(CanInteractSeekSlider));
                break;
            case nameof(IAudioService.Status):
            case nameof(IAudioService.IsSeekBuffering):
                OnPropertyChanged(nameof(CanInteractSeekSlider));
                break;
        }
    }

    private void UpdateFromPlayer(double position)
    {
        _isUpdatingFromPlayer = true;
        try
        {
            SeekPosition = position;
        }
        finally
        {
            _isUpdatingFromPlayer = false;
        }
    }
}
