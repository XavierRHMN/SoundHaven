using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using SoundHaven.Models;
using SoundHaven.Services;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace SoundHaven.ViewModels
{
    public class SongInfoViewModel : ViewModelBase, IDisposable
    {
        private readonly PlaybackViewModel _playbackViewModel;
        private Song _currentSong;
        private double _titleScrollPosition1;
        private double _titleScrollPosition2;
        private double _textWidth;
        private DispatcherTimer _scrollTimer;
        private AudioService _audioService;
        private bool _isScrollingNeeded;
        private DispatcherTimer _pauseTimer;
        private bool _isPaused;
        private bool _hasPausedThisCycle;

        public Song CurrentSong
        {
            get => _currentSong;
            private set
            {
                if (_currentSong != value)
                {
                    _currentSong = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentSongExists));
                    _hasPausedThisCycle = false;

                    // Update text width based on the new song title
                    TextWidth = ExtractTextWidth(CurrentSong?.Title, "Circular", 17);

                    UpdateScrollingState();
                }
            }
        }

        public bool CurrentSongExists => CurrentSong != null;

        private bool _isSeekBuffering;
        public bool IsSeekBuffering
        {
            get => _isSeekBuffering;
            set
            {
                if (_isSeekBuffering != value)
                {
                    _isSeekBuffering = value;
                    OnPropertyChanged();
                }
            }
        }

        public double TitleScrollPosition1
        {
            get => _titleScrollPosition1;
            set
            {
                _titleScrollPosition1 = value;
                OnPropertyChanged();
            }
        }

        public double TitleScrollPosition2
        {
            get => _titleScrollPosition2;
            set
            {
                _titleScrollPosition2 = value;
                OnPropertyChanged();
            }
        }

        public double TextWidth
        {
            get => _textWidth;
            set
            {
                _textWidth = value;
                OnPropertyChanged();
                UpdateScrollingState();
            }
        }

        public double ControlWidth { get; set; } = 200 * 2; // Width of the Song Info Control

        public SongInfoViewModel(PlaybackViewModel playbackViewModel, AudioService audioService)
        {
            _playbackViewModel = playbackViewModel ?? throw new ArgumentNullException(nameof(playbackViewModel));
            _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
            _playbackViewModel.PropertyChanged += PlaybackViewModel_PropertyChanged;
            _audioService.PropertyChanged += AudioService_PropertyChanged;

            CurrentSong = _playbackViewModel.CurrentSong;
            InitializeScrollTimer();
            InitializePauseTimer();
        }

        private void PlaybackViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlaybackViewModel.CurrentSong))
            {
                CurrentSong = _playbackViewModel.CurrentSong;
            }
        }

        private void AudioService_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AudioService.IsSeekBuffering))
            {
                IsSeekBuffering = !_playbackViewModel.IsPlaying && _audioService.IsSeekBuffering && _currentSong?.VideoId != null;
            }
        }
        
        private void InitializePauseTimer()
        {
            _pauseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _pauseTimer.Tick += (sender, e) =>
            {
                _isPaused = false;
                _pauseTimer.Stop();
            };
        }

        private void InitializeScrollTimer()
        {
            _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _scrollTimer.Tick += (sender, e) => ScrollText();
        }

        private void UpdateScrollingState()
        {
            _isScrollingNeeded = TextWidth > ControlWidth / 2;
            Console.WriteLine(TextWidth + "/" + ControlWidth / 2);

            if (_isScrollingNeeded)
            {
                InitializeScrollPositions();
                if (!_scrollTimer.IsEnabled)
                {
                    _scrollTimer.Start();
                }
            }
            else
            {
                _scrollTimer.Stop();
                ResetScrollPositions();
            }
        }

        private void InitializeScrollPositions()
        {
            const double spacing = 50; // Adjust spacing as needed
            const double initialAdjustment = 50;

            TitleScrollPosition1 = initialAdjustment;
            TitleScrollPosition2 = TextWidth + initialAdjustment + spacing;
        }

        private void ResetScrollPositions()
        {
            TitleScrollPosition1 = 0;
            TitleScrollPosition2 = ControlWidth; // Move the second instance out of view
        }

        private void ScrollText()
        {
            if (!_isScrollingNeeded || _isPaused) return;

            const double scrollSpeed = 2; // Adjust scroll speed as needed
            const double spacing = 50; // Spacing between the two TextBlocks

            // Update both scroll positions
            TitleScrollPosition1 -= scrollSpeed;
            TitleScrollPosition2 -= scrollSpeed;

            // Check if either position has reached 0 and we haven't paused in this cycle
            if (!_hasPausedThisCycle && 
                (Math.Abs(TitleScrollPosition1) < scrollSpeed || Math.Abs(TitleScrollPosition2) < scrollSpeed))
            {
                _isPaused = true;
                _hasPausedThisCycle = true;
                _pauseTimer.Start();
            }
            
            // Reset the first TextBlock if it's completely out of view
            if (TitleScrollPosition1 <= -TextWidth)
            {
                TitleScrollPosition1 = TitleScrollPosition2 + TextWidth + spacing;
                _hasPausedThisCycle = false; 
            }

            // Reset the second TextBlock if it's completely out of view
            if (TitleScrollPosition2 <= -TextWidth)
            {
                TitleScrollPosition2 = TitleScrollPosition1 + TextWidth + spacing;
                _hasPausedThisCycle = false; 
            }
        }

        private double ExtractTextWidth(string text, string fontFamily, double fontSize)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            var typeface = new Typeface(fontFamily);
            var formattedText = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.Black
            );

            return formattedText.Width;
        }

        public void Dispose()
        {
            _playbackViewModel.PropertyChanged -= PlaybackViewModel_PropertyChanged;
            _audioService.PropertyChanged -= AudioService_PropertyChanged;
            _pauseTimer?.Stop();
            _pauseTimer = null;
            _scrollTimer?.Stop();
            _scrollTimer = null;
        }
    }
}