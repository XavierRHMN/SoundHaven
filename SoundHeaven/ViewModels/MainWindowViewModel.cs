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
using System.IO;
using System.Reactive;
using System.Threading.Tasks;

namespace SoundHeaven.ViewModels
{
    public class MainWindowViewModel : ViewModelBase, INotifyPropertyChanged
    {
        public ObservableCollection<Song> SongCollection => _songStore.Songs;
        private readonly AudioPlayerService _audioPlayerService;
        private readonly SongStore _songStore;
        
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
        
        public ObservableCollection<Playlist> Playlists { get; set; }

        // Commands
        public RelayCommand PlayCommand { get; }
        public RelayCommand PauseCommand { get; }
        public RelayCommand NextCommand { get; }
        public RelayCommand PreviousCommand { get; }
        public RelayCommand ShowHomeViewCommand { get; }
        public RelayCommand ShowPlaylistViewCommand { get; }

        // Constructor
        public MainWindowViewModel()
        {
            CurrentViewModel = new HomeViewModel();
            
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

            // Load initial data (optional step)
            _songStore.LoadSongs();
        }
        
        public void ShowHomeView() {
            CurrentViewModel = new HomeViewModel();
        }
        
        public void ShowPlaylistView() {
            CurrentViewModel = new PlaylistViewModel();
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

        // INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
