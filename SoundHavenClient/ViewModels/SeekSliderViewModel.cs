using Avalonia.Threading;
using SoundHaven.Services;
using System;
using System.ComponentModel;
using System.Diagnostics;

namespace SoundHaven.ViewModels
{
    public class SeekSliderViewModel : ViewModelBase, IDisposable
    {
        private readonly AudioService _audioService;
        private readonly PlaybackViewModel _playbackViewModel;
        private DispatcherTimer _seekTimer;
        private DispatcherTimer _debounceTimer;
        private double _seekPosition;
        private bool _isUpdatingFromTimer;
        private double _maximumSeekValue;
        private bool _isUserSeeking;

        public double MaximumSeekValue
        {
            get => _audioService.TotalDuration.TotalSeconds;
            set => SetProperty(ref _maximumSeekValue, value);
        }
        
        public double SeekPosition
        {
            get => _seekPosition;
            set
            {
                if (SetProperty(ref _seekPosition, value) && !_isUpdatingFromTimer)
                {
                    _isUserSeeking = true;
                    _debounceTimer.Stop();
                    _debounceTimer.Start();
                }
            }
        }
        
        private bool _canPlaybackControl;
        public bool CanPlaybackControl
        {
            get => _canPlaybackControl;
            set => SetProperty(ref _canPlaybackControl, value);
        }

        public SeekSliderViewModel(AudioService audioService, PlaybackViewModel playbackViewModel)
        {
            _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
            _playbackViewModel = playbackViewModel ?? throw new ArgumentNullException(nameof(playbackViewModel));

            _audioService.PropertyChanged += AudioService_PropertyChanged;
            _playbackViewModel.PropertyChanged += PlaybackViewModelPropertyChanged;
            _playbackViewModel.SeekPositionReset += OnSeekPositionReset;
            InitializeSeekTimer();
            InitializeDebounceTimer();
        }

        private void InitializeSeekTimer()
        {
            _seekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _seekTimer.Tick += UpdateSeekPosition;
            _seekTimer.Start();
        }

        private void InitializeDebounceTimer()
        {
            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                OnSeekPositionChanged(SeekPosition);
                _isUserSeeking = false;
            };
        }

        private void UpdateSeekPosition(object sender, EventArgs e)
        {
            if (_playbackViewModel.CurrentSong == null || _isUserSeeking || _playbackViewModel.IsTransitioningTracks)
            {
                return;
            }

            _isUpdatingFromTimer = true;
            SeekPosition = (_playbackViewModel.CurrentSong.IsYouTubeVideo
                ? _audioService.CurrentYoutubePosition
                : _audioService.CurrentLocalPosition).TotalSeconds;
            _isUpdatingFromTimer = false;
        }

        private void OnSeekPositionChanged(double newPosition)
        {
            _audioService.Seek(TimeSpan.FromSeconds(newPosition));
            if (!_audioService.IsPaused)
            {
                _seekTimer.Start();
            }
        }

        private void OnSeekPositionReset(object sender, EventArgs e)
        {
            SeekPosition = 0;
        }

        private void PlaybackViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlaybackViewModel.CurrentSong))
            {
                OnPropertyChanged(nameof(MaximumSeekValue));
            }
        }

        public override void Dispose()
        {
            _audioService.PropertyChanged -= AudioService_PropertyChanged;
            _playbackViewModel.PropertyChanged -= PlaybackViewModelPropertyChanged;
            _playbackViewModel.SeekPositionReset -= OnSeekPositionReset;
            _seekTimer.Stop();
            _seekTimer = null;
            _debounceTimer.Stop();
            _debounceTimer = null;
        }

        private void AudioService_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AudioService.IsPaused))
            {
                if (_audioService.IsPaused)
                {
                    _seekTimer.Stop();
                }
                else
                {
                    _seekTimer.Start();
                }
            }
            else if (e.PropertyName == nameof(AudioService.TotalDuration))
            {
                OnPropertyChanged(nameof(MaximumSeekValue));
            }
        }
    }
}
