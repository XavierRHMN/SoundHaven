﻿using Avalonia.Threading;
using SoundHeaven.Services;
using System;
using System.ComponentModel;
using System.Diagnostics;

namespace SoundHeaven.ViewModels
{
    public class SeekSliderViewModel : ViewModelBase, IDisposable
    {
        private readonly AudioService _audioService;
        private readonly MainWindowViewModel _mainViewModel;
        private readonly PlaybackViewModel _playbackViewModel;
        private DispatcherTimer _seekTimer;
        private DispatcherTimer _debounceTimer;
        private double _seekPosition;
        private bool _isUpdatingFromTimer;
        public double MaximumSeekValue => _mainViewModel.CurrentSong?.Length ?? 0;
        
        public SeekSliderViewModel(MainWindowViewModel mainViewModel, AudioService audioService, PlaybackViewModel playbackViewModel)
        {
            _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
            _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
            _playbackViewModel = playbackViewModel ?? throw new ArgumentException(nameof(playbackViewModel));
            
            _mainViewModel.PropertyChanged += MainViewModelPropertyChanged;
            InitializeSeekTimer();
            InitializeDebounceTimer();
        }

        public double SeekPosition
        {
            get => _seekPosition;
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
            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                OnSeekPositionChanged(SeekPosition);
            };
        }

        private void UpdateSeekPosition(object sender, EventArgs e)
        {
            if (_mainViewModel.CurrentSong != null && !_debounceTimer.IsEnabled)
            {
                _isUpdatingFromTimer = true;
                var currentTime = _audioService.GetCurrentTime();
                SeekPosition = currentTime.TotalSeconds;
                _isUpdatingFromTimer = false;
                Debug.WriteLine($"UpdateSeekPosition: {SeekPosition}");
            }

            if (_audioService.IsStopped())
            {
                _playbackViewModel.IsPlaying = false;
            }
        }

        private void OnSeekPositionChanged(double newPosition)
        {
            Debug.WriteLine($"OnSeekPositionChanged: {newPosition}");
            _audioService.Seek(TimeSpan.FromSeconds(newPosition));
        }

        private void MainViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentSong))
            {
                OnPropertyChanged(nameof(MaximumSeekValue));
            }
        }

        public void Dispose()
        {
            _mainViewModel.PropertyChanged -= MainViewModelPropertyChanged;
            _seekTimer.Stop();
            _seekTimer = null;
            _debounceTimer.Stop();
            _debounceTimer = null;
        }
    }
}