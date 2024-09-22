using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ReactiveUI;
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
        public PlaylistViewModel PlaylistViewModel { get; set; }
        public HomeViewModel HomeViewModel { get; set; }

        public ObservableCollection<Playlist> PlaylistCollection => PlaylistStore.Playlists;

        public AudioPlayerService AudioService { get; set; }
        public PlaylistStore PlaylistStore { get; set; }
        private readonly SongStore _songStore;
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
        private double _titleScrollPosition = 200;
        public double TitleScrollPosition
        {
            get => _titleScrollPosition;
            set
            {
                _titleScrollPosition = value;
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

        public ToolBarControlViewModel ToolBarControlViewModel { get; set; }
        public PlaybackControlViewModel PlaybackControlViewModel { get; set; }
        public ShuffleControlViewModel ShuffleControlViewModel { get; }

        public double TextWidth => ExtractTextWidth(CurrentSong?.Title, "Nunito", 15);

        // public double TextWidth { get; set; } = 200; // Estimated width of the text
        public double ControlWidth { get; set; } = 200; // Width of the canvas/border

        // Commands
        public RelayCommand MuteCommand { get; }


        public MainWindowViewModel()
        {
            AudioService = new AudioPlayerService();
            _songStore = new SongStore();
            PlaylistStore = new PlaylistStore(this);
            PlaylistViewModel = new PlaylistViewModel(this, new OpenFileDialogService());
            HomeViewModel = new HomeViewModel(this);

            MuteCommand = new RelayCommand(ToggleMute, CanToggleMute);

            _songStore.LoadSongs();
            InitializeSeekTimer();
            InitializeScrollTimer();

            var example = new Playlist()
            {
                Name = "example",
                Songs = _songStore.Songs
            };
            PlaylistStore.AddPlaylist(example);

            ToolBarControlViewModel = new ToolBarControlViewModel(this);
            PlaybackControlViewModel = new PlaybackControlViewModel(this);
            ShuffleControlViewModel = new ShuffleControlViewModel(this);


            // Set initial CurrentViewModel
            CurrentViewModel = PlaylistViewModel;
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

        private void ScrollText()
        {
            // Adjust the scroll positions
            TitleScrollPosition -= 2;

            // Reset Title Scroll Position when it goes off screen
            if (TitleScrollPosition <= -TextWidth)
            {
                TitleScrollPosition = ControlWidth;
            }
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
