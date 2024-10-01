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
        public double MaximumSeekValue
        {
            get
            {
                return _audioService.TotalDuration.TotalSeconds;
            }
            set
            {
                if (_maximumSeekValue != value)
                {
                    _maximumSeekValue = value;
                    OnPropertyChanged();
                }
            }
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

        public double SeekPosition
        {
            get
            {
                return _seekPosition;
            }
            set
            {
                if (SetProperty(ref _seekPosition, value) && !_isUpdatingFromTimer)
                {
                    _debounceTimer.Stop();
                    _debounceTimer.Start();
                }
            }
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
            };
        }

        private void UpdateSeekPosition(object sender, EventArgs e)
        {
            if (_audioService.IsPaused || _debounceTimer.IsEnabled || _playbackViewModel.CurrentSong == null)
            {
                return;
            }
            
            _isUpdatingFromTimer = true;
            SeekPosition = (_playbackViewModel.CurrentSong.VideoId != null
                ? _audioService.CurrentPosition // Get YouTube video position
                : _audioService.GetCurrentTime()).TotalSeconds; // Get local file's time
            _isUpdatingFromTimer = false;
        }

        private void OnSeekPositionChanged(double newPosition)
        {
            // Console.WriteLine($"OnSeekPositionChanged: {newPosition}");
            _audioService.Seek(TimeSpan.FromSeconds(newPosition));
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
                    _seekTimer.Stop(); // Stop updating seek position when paused
                }
                else
                {
                    _seekTimer.Start(); // Resume updating when playback resumes
                }
            }
            else if (e.PropertyName == nameof(AudioService.TotalDuration))
            {
                OnPropertyChanged(nameof(MaximumSeekValue));
            }
        }
    }
}
