using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SoundHeaven.Models;
using SoundHeaven.Commands;
using SoundHeaven.Services;
using System;

namespace SoundHeaven.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        // Services
        private readonly AudioPlayerService _audioPlayerService;

        // Properties
        private Song _currentSong;
        private bool _isPaused = false;
        private bool _isPlaying = true;

        public Song CurrentSong
        {
            get => _currentSong;
            set
            {
                if (_currentSong != value)
                {
                    _currentSong = value;
                    OnPropertyChanged();
                    OnCurrentSongChanged();
                    Play(); // Automatically play the new song when it's set
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

        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                if (_isPaused != value)
                {
                    _isPaused = value;
                    OnPropertyChanged();
                    OnIsPlayingChanged();
                }
            }
        }

        public ObservableCollection<Song> Songs { get; set; }
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
            Songs = new ObservableCollection<Song>();
            Playlists = new ObservableCollection<Playlist>();

            // Initialize commands
            PlayCommand = new RelayCommand(Play, CanPlay);
            PauseCommand = new RelayCommand(Pause, CanPause);
            NextCommand = new RelayCommand(Next, CanNext);
            PreviousCommand = new RelayCommand(Previous, CanPrevious);

            // Load initial data (optional step)
            LoadSongs();
        }

        private void Play()
        {
            if (CurrentSong != null)
            {
                if (IsPaused)
                {
                    // If the song is paused, resume it
                    _audioPlayerService.Resume();
                    IsPlaying = true;
                    IsPaused = false;
                }
                else
                {
                    // Otherwise, start playing the current song from the beginning
                    _audioPlayerService.Play(CurrentSong.FilePath);

                }
            }
        }

        private bool CanPlay()
        {
            // Can play if there is a current song and either nothing is playing or the song is paused
            return CurrentSong != null;
        }

        private void Pause()
        {
            Console.WriteLine(IsPlaying);
            if (IsPlaying)
            {
                _audioPlayerService.Pause();
                IsPlaying = false;
                IsPaused = true; // Mark the song as paused so it can be resumed
            }
        }

        private bool CanPause()
        {
            // Can pause only if a song is playing
            return IsPlaying;
        }

        private void Next()
        {
            // Logic for playing the next song, e.g., move to the next song in the list
        }

        private bool CanNext()
        {
            return true; // CanNext logic to determine if the next song is available
        }

        private void Previous()
        {
            // Logic for playing the previous song, e.g., move to the previous song in the list
        }

        private bool CanPrevious()
        {
            return true; // CanPrevious logic to determine if the previous song is available
        }

        // Load initial data (optional)
        private void LoadSongs()
        {
            // Get the base directory of the executable
            string projectDirectory = AppContext.BaseDirectory;

            // Set the relative path to the Tracks folder in your project
            string tracksPath = System.IO.Path.Combine(projectDirectory, "..", "..", "..", "Tracks");
            
            // Load songs using the relative path to the Tracks folder
            Songs.Add(new Song
            {
                Title = "Song 1",
                Artist = "Artist A",
                Album = "Album A",
                FilePath = System.IO.Path.Combine(tracksPath, "Counting Stars.mp3")
            });

            Songs.Add(new Song
            {
                Title = "Song 2",
                Artist = "Artist B",
                Album = "Album B",
                FilePath = System.IO.Path.Combine(tracksPath, "Nujabes - Highs 2 Lows.mp3")
            });
        }

        // Methods to handle property changes that affect command executability
        private void OnCurrentSongChanged()
        {
            PlayCommand.RaiseCanExecuteChanged();
            NextCommand.RaiseCanExecuteChanged();
            PreviousCommand.RaiseCanExecuteChanged();
        }

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
