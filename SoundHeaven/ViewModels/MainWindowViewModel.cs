using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using ReactiveUI;
using SoundHeaven.Commands;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SoundHeaven.Models;
using SoundHeaven.Services;
using SoundHeaven.Stores;
using System;

namespace SoundHeaven.ViewModels
{
    public class MainWindowViewModel : ViewModelBase, INotifyPropertyChanged
    {
        public ObservableCollection<Song> SongCollection => _songStore.Songs;
        private readonly AudioPlayerService _audioPlayerService;
        public AudioPlayerService AudioPlayerService => _audioPlayerService;
        private readonly SongStore _songStore;
        private DispatcherTimer _seekTimer;
        private DispatcherTimer _scrollTimer;
        private double _textWidth;
        private double _controlWidth;
        
        private ViewModelBase _currentViewModel;
        public ViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            set
            {
                if (_currentViewModel != value)
                {
                    _currentViewModel = value;
                    OnPropertyChanged();
                }
            }
        }

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
                    Start();
                    SeekPosition = 0;
                    Volume = _audioPlayerService.GetCurrentVolume();
                    MuteCommand.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _isPlaying = true;
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged();
                    OnIsPlayingChanged();
                }
            }
        }

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
                    _audioPlayerService?.SetVolume((float)_volume);
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
        private double _titleScrollPosition;
        public double TitleScrollPosition
        {
            get => _titleScrollPosition;
            set
            {
                _titleScrollPosition = value;
                OnPropertyChanged();
            }
        }

        private double _artistScrollPosition;
        public double ArtistScrollPosition
        {
            get => _artistScrollPosition;
            set
            {
                _artistScrollPosition = value;
                OnPropertyChanged();
            }
        }
        
        public double TextWidth { get; set; } = 200; // Estimated width of the text
        public double ControlWidth { get; set; } = 300; // Width of the canvas/border

        public ObservableCollection<Playlist> Playlists { get; set; }

        // Commands
        public RelayCommand PlayCommand { get; }
        public RelayCommand PauseCommand { get; }
        public RelayCommand NextCommand { get; }
        public RelayCommand PreviousCommand { get; }
        public RelayCommand ShowHomeViewCommand { get; }
        public RelayCommand ShowPlaylistViewCommand { get; }
        public RelayCommand MuteCommand { get; }

        public MainWindowViewModel()
        {
            _audioPlayerService = new AudioPlayerService();
            _songStore = new SongStore();
            Playlists = new ObservableCollection<Playlist>();

            PlayCommand = new RelayCommand(Play, CanPlay);
            PauseCommand = new RelayCommand(Pause, CanPause);
            NextCommand = new RelayCommand(Next, CanNext);
            PreviousCommand = new RelayCommand(Previous, CanPrevious);
            ShowHomeViewCommand = new RelayCommand(ShowHomeView);
            ShowPlaylistViewCommand = new RelayCommand(ShowPlaylistView);
            MuteCommand = new RelayCommand(ToggleMute, CanToggleMute);

            CurrentViewModel = new HomeViewModel(this);

            _songStore.LoadSongs();
            InitializeSeekTimer();
            InitializeScrollTimer();
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
            ArtistScrollPosition -= 2;

            // Reset Title Scroll Position when it goes off screen
            if (TitleScrollPosition <= -TextWidth)
            {
                TitleScrollPosition = ControlWidth;
            }

            // Reset Artist Scroll Position when it goes off screen
            if (ArtistScrollPosition <= -TextWidth)
            {
                ArtistScrollPosition = ControlWidth;
            }
        }

        private void UpdateSeekPosition(object sender, EventArgs e)
        {
            if (CurrentSong != null)
            {
                SeekPosition += 0.1;
            }
        }

        public void ShowHomeView() => CurrentViewModel = new HomeViewModel(this);
        public void ShowPlaylistView() => CurrentViewModel = new PlaylistViewModel(this);

        private void Start()
        {
            if (CurrentSong != null)
            {
                _audioPlayerService.Play(CurrentSong.FilePath);
                IsPlaying = true;
            }
        }

        private void Play()
        {
            if (!IsPlaying)
            {
                _audioPlayerService.Resume();
                IsPlaying = true;
            }
        }

        private bool CanPlay() => !IsPlaying;

        private void Pause()
        {
            if (IsPlaying)
            {
                _audioPlayerService.Pause();
                IsPlaying = false;
            }
        }

        private bool CanPause() => IsPlaying;

        private void Next()
        {
            var nextSong = _songStore.NextSong();
            if (nextSong != null)
            {
                CurrentSong = nextSong;
                Start();
                SeekPosition = 0;
            }
        }

        private bool CanNext() => _songStore.CanNext;

        private void Previous()
        {
            var prevSong = _songStore.PreviousSong();
            if (prevSong != null)
            {
                CurrentSong = prevSong;
                Start();
                SeekPosition = 0;
            }
        }

        private bool CanPrevious() => _songStore.CanPrevious;

        private void OnIsPlayingChanged()
        {
            PlayCommand.RaiseCanExecuteChanged();
            PauseCommand.RaiseCanExecuteChanged();
        }

        private void ToggleMute() => IsMuted = !IsMuted;

        private bool CanToggleMute() => CurrentSong != null;
    }
}
