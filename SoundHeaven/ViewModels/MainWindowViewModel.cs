using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SoundHeaven.Models;
using SoundHeaven.Commands;
using SoundHeaven.Helpers;
using SoundHeaven.Services;
using SoundHeaven.Stores;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;

namespace SoundHeaven.ViewModels
{
    public class MainWindowViewModel : ViewModelBase, INotifyPropertyChanged
    {
        public ObservableCollection<Song> SongCollection => _songStore.Songs;
        private readonly AudioPlayerService _audioPlayerService;
        public AudioPlayerService AudioPlayerService => _audioPlayerService;
        private readonly SongStore _songStore;
        private double _initialVolume = 0.5;
        private DispatcherTimer _timer;
        private bool _isDragging = false;
        
        private ViewModelBase _currentViewModel;
        public ViewModelBase CurrentViewModel {
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
                    OnPropertyChanged();  // Notify UI about the change
                    Start();
                    Volume = _audioPlayerService.GetCurrentVolume(); // Assuming GetCurrentVolume() returns the current volume.
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
                
                // Only attempt to set volume if a song is loaded
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
                        PreviousVolume = (float)Volume; // Save the current volume before muting
                        Volume = 0; // Set volume to 0 when muted
                    }
                    else
                    {
                        Volume = PreviousVolume; // Restore the previous volume when unmuted
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
                    OnPropertyChanged();  // This will notify the ViewModel and the UI

                    if (CurrentSong != null)
                    {
                        // _audioPlayerService?.Seek(TimeSpan.FromSeconds(_seekPosition)); // seeks constantly
                    }
                }
            }
        }

        
        public ObservableCollection<Playlist> Playlists { get; set; }

        // Commands
        public RelayCommand PlayCommand { get; }
        public RelayCommand PauseCommand { get; }
        public RelayCommand NextCommand { get; }
        public RelayCommand PreviousCommand { get; }
        public RelayCommand ShowHomeViewCommand { get; }
        public RelayCommand ShowPlaylistViewCommand { get; }
        public RelayCommand MuteCommand { get; }
        public RelayCommand OnThumbDragStartedCommand { get; }
        public RelayCommand OnThumbDragCompletedCommand { get; }

        // Constructor
        public MainWindowViewModel()
        {
            // Initialize services
            _audioPlayerService = new AudioPlayerService();

            // Initialize collections
            _songStore = new SongStore();    
            Playlists = new ObservableCollection<Playlist>();

            // Initialize commands
            PlayCommand = new RelayCommand(Play, CanPlay);
            PauseCommand = new RelayCommand(Pause, CanPause);
            NextCommand = new RelayCommand(Next, CanNext);
            PreviousCommand = new RelayCommand(Previous, CanPrevious);
            ShowHomeViewCommand = new RelayCommand(ShowHomeView);
            ShowPlaylistViewCommand = new RelayCommand(ShowPlaylistView);
            CurrentViewModel = new HomeViewModel(this);
            MuteCommand = new RelayCommand(ToggleMute, CanToggleMute);
            OnThumbDragStartedCommand = new RelayCommand(OnThumbDragStarted);
            OnThumbDragCompletedCommand = new RelayCommand(OnThumbDragCompleted);

            // Load initial data (optional step)
            _songStore.LoadSongs();

            InitializeTimer();
        }

        private void InitializeTimer() {
            // Initialize the timer
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(500); // Update every 500ms
            _timer.Tick += UpdateSeekPosition;
            _timer.Start();
        }
        
        private void UpdateSeekPosition(object sender, EventArgs e)
        {
            if (CurrentSong != null && AudioPlayerService != null)
            {
                SeekPosition += 0.5;
            }
        }

        private void OnThumbDragStarted() {
            _isDragging = true;
        }
        
        private void OnThumbDragCompleted() {
            _isDragging = false;
        }
        
        public void ShowHomeView() {
            CurrentViewModel = new HomeViewModel(this);
        }
        
        public void ShowPlaylistView() {
            CurrentViewModel = new PlaylistViewModel(this);
        }
                
        private void Start()
        {
            if (CurrentSong != null)
            {
                // Play the song
                _audioPlayerService.Play(CurrentSong.FilePath);
                IsPlaying = true;
            }
        }

        private void Play()
        {
            if (!IsPlaying)
            {
                // If the song is paused, resume it
                _audioPlayerService.Resume();
                IsPlaying = true;
            }
        }

        private bool CanPlay()
        {
            // Can play if the song is paused
            return !IsPlaying;
        }

        private void Pause()
        {
            if (IsPlaying)
            {
                _audioPlayerService.Pause();
                IsPlaying = false;
            }
        }

        private bool CanPause()
        {
            // Can pause only if a song is playing
            return IsPlaying;
        }

        // Navigate to the next song
        private void Next()
        {
            var nextSong = _songStore.NextSong();
            if (nextSong != null)
            {
                CurrentSong = nextSong;
                Start();
            }
        }
        
        private bool CanNext() => _songStore.CanNext;

        // Navigate to the previous song
        private void Previous()
        {
            var prevSong = _songStore.PreviousSong();
            if (prevSong != null)
            {
                CurrentSong = prevSong;
                Start();
            }
        }
        private bool CanPrevious() => _songStore.CanPrevious;

        private void OnIsPlayingChanged()
        {
            PlayCommand.RaiseCanExecuteChanged();
            PauseCommand.RaiseCanExecuteChanged();
        }
        
        private void ToggleMute()
        {
            IsMuted = !IsMuted;
        }

        private bool CanToggleMute() {
            return CurrentSong != null;
        }

        // INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
