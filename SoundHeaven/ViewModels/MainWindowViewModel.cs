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

namespace SoundHeaven.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public ObservableCollection<Song> SongCollection => _songStore.Songs;
        
        // Services
        private readonly AudioPlayerService _audioPlayerService;
        private readonly SongStore _songStore;

        // Properties
        private Song _currentSong;
        private bool _isPlaying = true;

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

            // Load initial data (optional step)
            _songStore.LoadSongs();
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
    }
}
