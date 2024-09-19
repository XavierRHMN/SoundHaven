using Avalonia;
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
using System;
using System.Globalization;
using System.Windows.Input;

namespace SoundHeaven.ViewModels
{
    public class MainWindowViewModel : ViewModelBase, INotifyPropertyChanged
    {
        
        public Playlist CurrentPlaylist
        {
            get => _playlistStore.CurrentPlaylist;
            set => _playlistStore.CurrentPlaylist = value;
        }
        public ICommand CreatePlaylistCommand { get; }
        public ObservableCollection<Playlist> PlaylistCollection
        {
            get
            {
                return _playlistStore.Playlists;
            }
        }
        

        public ObservableCollection<Song> SongCollection
        {
            get
            {
                return _songStore.Songs;
            }
        }
        private readonly AudioPlayerService _audioPlayerService;
        public AudioPlayerService AudioPlayerService
        {
            get
            {
                return _audioPlayerService;
            }
        }
        
        private readonly PlaylistStore _playlistStore;
        private readonly SongStore _songStore;
        private DispatcherTimer _seekTimer;
        private DispatcherTimer _scrollTimer;

        private ViewModelBase _currentViewModel;
        public ViewModelBase CurrentViewModel
        {
            get
            {
                return _currentViewModel;
            }
            set
            {
                if (_currentViewModel != value)
                {
                    _currentViewModel = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public bool CurrentSongExists => CurrentSong != null;
        private Song _currentSong;
        public Song CurrentSong
        {
            get
            {
                return _currentSong;
            }
            set
            {
                if (_currentSong != value)
                {
                    _currentSong = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentSongExists));
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
            get
            {
                return _isPlaying;
            }
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
            get
            {
                return _volume;
            }
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
            get
            {
                return _isMuted;
            }
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
            get
            {
                return _previousVolume;
            }
            set
            {
                _previousVolume = value;
                OnPropertyChanged();
            }
        }

        private double _seekPosition;
        public double SeekPosition
        {
            get
            {
                return _seekPosition;
            }
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
            get
            {
                return _titleScrollPosition;
            }
            set
            {
                _titleScrollPosition = value;
                OnPropertyChanged();
            }
        }

        public double TextWidth => ExtractTextWidth(CurrentSong?.Title, "Nunito", 15);

        // public double TextWidth { get; set; } = 200; // Estimated width of the text
        public double ControlWidth { get; set; } = 200; // Width of the canvas/border
        
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
            _playlistStore = new PlaylistStore();

            PlayCommand = new RelayCommand(Play, CanPlay);
            PauseCommand = new RelayCommand(Pause, CanPause);
            NextCommand = new RelayCommand(Next, CanNext);
            PreviousCommand = new RelayCommand(Previous, CanPrevious);
            ShowHomeViewCommand = new RelayCommand(ShowHomeView);
            ShowPlaylistViewCommand = new RelayCommand(ShowPlaylistView);
            MuteCommand = new RelayCommand(ToggleMute, CanToggleMute);

            CurrentViewModel = new HomeViewModel(this);
            CreatePlaylistCommand = new RelayCommand(CreatePlaylist);
            
            _songStore.LoadSongs();
            InitializeSeekTimer();
            InitializeScrollTimer();
            
            // Add an example playlist
            var examplePlaylist = new Playlist
            {
                Name = "Example Playlist",
                Songs = new ObservableCollection<Song>
                {
                    new Song { Title = "Song 1", Artist = "Artist 1", Duration = TimeSpan.FromMinutes(3), FilePath = "path/to/song1.mp3" },
                    new Song { Title = "Song 2", Artist = "Artist 2", Duration = TimeSpan.FromMinutes(4), FilePath = "path/to/song2.mp3" }
                }
            };
            
            var anotherExamplePlaylist = new Playlist
            {
                Name = "Another Example Playlist",
                Songs = new ObservableCollection<Song>
                {
                    new Song { Title = "Song A", Artist = "Artist A", Duration = TimeSpan.FromMinutes(3), FilePath = "path/to/songA.mp3" },
                    new Song { Title = "Song B", Artist = "Artist B", Duration = TimeSpan.FromMinutes(4), FilePath = "path/to/songB.mp3" }
                }
            };
            
            
            _playlistStore.AddPlaylist(examplePlaylist);
            _playlistStore.AddPlaylist(anotherExamplePlaylist);
            Console.WriteLine(_playlistStore.CurrentPlaylist);
        }
        
        private void CreatePlaylist()
        {
            // Logic for creating a new playlist
            var newPlaylist = new Playlist { Name = $"Playlist {PlaylistCollection.Count + 1}" };
            _playlistStore.AddPlaylist(newPlaylist);
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
                if (AudioPlayerService.IsStopped())
                {
                    IsPlaying = false;
                }
                else if (IsPlaying)
                {
                    SeekPosition += 0.1;
                }
            }
        }

        public void ShowHomeView() => CurrentViewModel = new HomeViewModel(this);
        public void ShowPlaylistView() => CurrentViewModel = new PlaylistViewModel(_playlistStore);


        public void Restart()
        {
            AudioPlayerService.Stop();
            Start();
            SeekPosition = 0;
        }
        
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
            if (AudioPlayerService.IsStopped())
            {
                Start();
                SeekPosition = 0;
            }
            
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

        public void Previous()
        {
            var prevSong = _songStore.PreviousSong();
            if (prevSong != null)
            {
                if (SeekPosition > 3)
                {
                    Restart();
                }
                else
                {
                    CurrentSong = prevSong;
                    Start();
                    SeekPosition = 0;
                }
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
