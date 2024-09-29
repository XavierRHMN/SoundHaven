using Avalonia.Media;
using Avalonia.Threading;
using SoundHaven.Models;
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

                    // Update text width based on the new song title
                    TextWidth = ExtractTextWidth(CurrentSong?.Title, "Circular", 15);

                    InitializeScrollPositions();
                }
            }
        }

        public bool CurrentSongExists => CurrentSong != null;

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
            }
        }

        public double ControlWidth { get; set; } = 200 * 2; // Width of the Song Info Control

        public SongInfoViewModel(PlaybackViewModel playbackViewModel)
        {
            _playbackViewModel = playbackViewModel ?? throw new ArgumentNullException(nameof(playbackViewModel));
            _playbackViewModel.PropertyChanged += PlaybackViewModel_PropertyChanged;

            // Initialize with the current song from PlaybackViewModel
            CurrentSong = _playbackViewModel.CurrentSong;

            InitializeScrollTimer();
        }

        private void PlaybackViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlaybackViewModel.CurrentSong))
            {
                CurrentSong = _playbackViewModel.CurrentSong;
            }
        }

        private void InitializeScrollTimer()
        {
            _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _scrollTimer.Tick += (sender, e) => ScrollText();
            _scrollTimer.Start();
        }

        private void InitializeScrollPositions()
        {
            const double spacing = 50; // Adjust spacing as needed
            const double spaceFromLeft = 200;

            TitleScrollPosition1 = spaceFromLeft;
            TitleScrollPosition2 = spaceFromLeft + TextWidth + spacing;
        }

        private void ScrollText()
        {
            const double scrollSpeed = 2; // Adjust scroll speed as needed
            const double spacing = 50; // Spacing between the two TextBlocks

            // Update both scroll positions
            TitleScrollPosition1 -= scrollSpeed;
            TitleScrollPosition2 -= scrollSpeed;

            // Reset the first TextBlock if it's completely out of view
            if (TitleScrollPosition1 <= -TextWidth)
            {
                TitleScrollPosition1 = TitleScrollPosition2 + TextWidth + spacing;
            }

            // Reset the second TextBlock if it's completely out of view
            if (TitleScrollPosition2 <= -TextWidth)
            {
                TitleScrollPosition2 = TitleScrollPosition1 + TextWidth + spacing;
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
            _scrollTimer?.Stop();
            _scrollTimer = null;
        }
    }
}
