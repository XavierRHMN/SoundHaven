using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using SoundHeaven.Commands;
using SoundHeaven.Converters;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SoundHeaven.Models;
using SoundHeaven.Services;
using SoundHeaven.Stores;
using SoundHeaven.Views;
using System;
using System.Globalization;
using System.Linq;
using System.Windows.Input;

namespace SoundHeaven.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public ObservableCollection<Playlist> PlaylistCollection => PlaylistStore.Playlists;

        public AudioPlayerService AudioService { get; set; }
        public PlaylistStore PlaylistStore { get; set; }
        private DispatcherTimer _seekTimer;
        private DispatcherTimer _scrollTimer;

        public bool CurrentSongExists => CurrentSong != null;
        private Song _currentSong;
        public Song CurrentSong
        {
            get => _currentSong;
            set
            {
                if (_currentSong != value)
                {
                    _currentSong = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentSongExists));
                    AudioService.Start(_currentSong.FilePath);
                    SeekPosition = 0;
                    Volume = AudioService.GetCurrentVolume();
                    MuteCommand.RaiseCanExecuteChanged();
                    PlaybackControlViewModel.IsPlaying = true;

                    // Update text width based on the new song title
                    TextWidth = ExtractTextWidth(CurrentSong?.Title, "Nunito", 14);
                    
                    // Initialize scroll positions
                    InitializeScrollPositions();
                }
            }
        }

        private Playlist? _currentPlaylist;
        public Playlist? CurrentPlaylist
        {
            get => _currentPlaylist;
            set
            {
                if (_currentPlaylist != value)
                {
                    _currentPlaylist = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isPlaying => AudioService.IsPlaying();

        private double _volume;
        public double Volume
        {
            get => _volume;
            set
            {
                _volume = value;
                OnPropertyChanged();
                if (CurrentSong != null)
                {
                    AudioService?.SetVolume((float)_volume);
                }
            }
        }

        private bool _isMuted;
        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                if (_isMuted != value)
                {
                    _isMuted = value;
                    OnPropertyChanged();

                    if (_isMuted)
                    {
                        PreviousVolume = (float)Volume;
                        Volume = 0;
                    }
                    else
                    {
                        Volume = PreviousVolume;
                    }
                }
            }
        }

        private float _previousVolume;
        public float PreviousVolume
        {
            get => _previousVolume;
            set
            {
                _previousVolume = value;
                OnPropertyChanged();
            }
        }

        private double _seekPosition;
        public double SeekPosition
        {
            get => _seekPosition;
            set
            {
                if (_seekPosition != value)
                {
                    _seekPosition = value;
                    OnPropertyChanged();
                }
            }
        }

        // For the scrolling text
        private double _titleScrollPosition1;
        public double TitleScrollPosition1
        {
            get => _titleScrollPosition1;
            set
            {
                _titleScrollPosition1 = value;
                OnPropertyChanged();
            }
        }

        private double _titleScrollPosition2;
        public double TitleScrollPosition2
        {
            get => _titleScrollPosition2;
            set
            {
                _titleScrollPosition2 = value;
                OnPropertyChanged();
            }
        }

        private ViewModelBase _currentViewModel;
        public ViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            set
            {
                if (_currentViewModel != value) // Check if the new value is different
                {
                    _currentViewModel = value;
                    OnPropertyChanged(nameof(CurrentViewModel)); // Notify the UI of the property change
                }
            }
        }

        public PlaylistViewModel PlaylistViewModel { get; set; }
        public HomeViewModel HomeViewModel { get; set; }
        public ToolBarControlViewModel ToolBarControlViewModel { get; set; }
        public PlaybackControlViewModel PlaybackControlViewModel { get; set; }
        public ShuffleControlViewModel ShuffleControlViewModel { get; }

        private double _textWidth;
        public double TextWidth
        {
            get => _textWidth;
            set
            {
                _textWidth = value;
                OnPropertyChanged();
            }
        }

        public double ControlWidth { get; set; } = 200 * 2; // Width of the canvas/border

        // Commands
        public RelayCommand MuteCommand { get; }


        public MainWindowViewModel()
        {
            // Initialize ApiKeyService
            IApiKeyProvider apiKeyProvider = new ApiKeyService("API_KEY.txt");
            string apiKey = apiKeyProvider.GetApiKey();
            var dataService = new LastFmDataService(apiKey);
            
            AudioService = new AudioPlayerService();
            PlaylistStore = new PlaylistStore(this);

            MuteCommand = new RelayCommand(ToggleMute, CanToggleMute);

            InitializeExamplePlaylist();

            ToolBarControlViewModel = new ToolBarControlViewModel(this);
            PlaybackControlViewModel = new PlaybackControlViewModel(this);
            ShuffleControlViewModel = new ShuffleControlViewModel(this);
            PlaylistViewModel = new PlaylistViewModel(this, new OpenFileDialogService());
            HomeViewModel = new HomeViewModel(this, dataService);

            // Set initial CurrentViewModel
            CurrentViewModel = HomeViewModel;

            // Initialize scroll positions
            InitializeScrollPositions();
        }

        private void InitializeExamplePlaylist()
        {
            SongStore songStore = new SongStore();
            songStore.LoadSongs();
            InitializeSeekTimer();
            InitializeScrollTimer();

            var example = new Playlist()
            {
                Name = "Playlist #1",
                Songs = songStore.Songs
            };
            PlaylistStore.AddPlaylist(example);
        }

        private void InitializeSeekTimer()
        {
            _seekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _seekTimer.Tick += UpdateSeekPosition;
            _seekTimer.Start();
        }

        private void InitializeScrollTimer()
        {
            _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _scrollTimer.Tick += (sender, e) => ScrollText();
            _scrollTimer.Start();
        }

        private void InitializeScrollPositions()
        {
            // Initialize the scroll positions based on the text width
            // Assuming the first TextBlock starts at 0 and the second starts after the first one with some spacing
            const double spacing = 50; // Adjust spacing as needed

            TitleScrollPosition1 = ControlWidth;
            TitleScrollPosition2 = ControlWidth + TextWidth + spacing;
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

            // Notify property changes
            OnPropertyChanged(nameof(TitleScrollPosition1));
            OnPropertyChanged(nameof(TitleScrollPosition2));
        }

        private void UpdateSeekPosition(object sender, EventArgs e)
        {
            if (CurrentSong != null)
            {
                if (AudioService.IsStopped())
                {
                    PlaybackControlViewModel.IsPlaying = false;
                }

                if (_isPlaying)
                {
                    SeekPosition += 0.1;
                }
            }
        }

        private void ToggleMute() => IsMuted = !IsMuted;

        private bool CanToggleMute() => CurrentSong != null;

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
    }
}
