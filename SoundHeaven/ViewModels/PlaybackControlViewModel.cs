using SoundHeaven.Commands;
using SoundHeaven.Models;
using SoundHeaven.Services;
using System;
using System.ComponentModel;

namespace SoundHeaven.ViewModels
{
    public class PlaybackControlViewModel : ViewModelBase
    {
        private AudioPlayerService _audioPlayerService => _mainWindowViewModel.AudioService;
        private readonly MainWindowViewModel _mainWindowViewModel;
        
                
        private bool _isShuffleEnabled;
        public bool IsShuffleEnabled
        {
            get => _isShuffleEnabled;
            set
            {
                if (_isShuffleEnabled != value)
                {
                    _isShuffleEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged(nameof(IsPlaying));
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
                    OnPropertyChanged(nameof(CurrentPlaylist));
                }
            }
        }

        public RelayCommand PlayCommand { get; private set; }
        public RelayCommand PauseCommand { get; private set; }
        public RelayCommand NextCommand { get; private set; }
        public RelayCommand PreviousCommand { get; private set; }

        public PlaybackControlViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;

            InitializeCommands();

            // Set initial CurrentPlaylist
            CurrentPlaylist = _mainWindowViewModel.CurrentPlaylist;

            // Subscribe to changes in MainWindowViewModel.CurrentPlaylist
            _mainWindowViewModel.PropertyChanged += MainWindowViewModel_PropertyChanged;
        }

        private void InitializeCommands()
        {
            PlayCommand = new RelayCommand(Play);
            PauseCommand = new RelayCommand(Pause);
            NextCommand = new RelayCommand(NextTrack);
            PreviousCommand = new RelayCommand(PreviousTrack);
        }

        private void Play()
        {
            var song = _mainWindowViewModel.CurrentSong;

            if (song != null)
            {
                if (_audioPlayerService.IsStopped())
                {
                    _audioPlayerService.Start(song.FilePath);
                }
                else
                {
                    _audioPlayerService.Resume();
                }
                IsPlaying = true;
            }
            else if (CurrentPlaylist?.Songs.Count > 0)
            {
                // Play the first song in the playlist
                song = CurrentPlaylist.Songs[0];
                _mainWindowViewModel.CurrentSong = song; // Update CurrentSong
                _audioPlayerService.Start(song.FilePath);
                IsPlaying = true;
            }
            else
            {
                Console.WriteLine("No song or playlist available to play.");
                IsPlaying = false;
            }
        }

        private bool CanPlay() => !IsPlaying && (CurrentPlaylist?.Songs.Count > 0 || _mainWindowViewModel.CurrentSong != null);

        private void Pause()
        {
            if (_audioPlayerService.IsPlaying())
            {
                _audioPlayerService.Pause();
                IsPlaying = false;
            }
        }

        private bool CanPause() => IsPlaying;

        private void NextTrack()
        {
            if (CurrentPlaylist != null)
            {
                Song? nextSong = null;

                if (IsShuffleEnabled)
                {
                    // Get a random song from the playlist
                    var random = new Random();
                    int index = random.Next(CurrentPlaylist.Songs.Count);
                    nextSong = CurrentPlaylist.Songs[index];
                }
                else
                {
                    // Get the next song in the playlist
                    nextSong = CurrentPlaylist.GetNextSong(_mainWindowViewModel.CurrentSong);
                }

                if (nextSong != null)
                {
                    _mainWindowViewModel.CurrentSong = nextSong;
                    _audioPlayerService.Start(nextSong.FilePath);
                    IsPlaying = true;
                }
                else
                {
                    Console.WriteLine("No next song available.");
                }
            }
            else
            {
                Console.WriteLine("No playlist available.");
            }
        }


        private bool CanNextTrack() => CurrentPlaylist?.Songs.Count > 1;

        private void PreviousTrack()
        {
            if (CurrentPlaylist == null)
            {
                Console.WriteLine("No playlist available.");
                return;
            }

            var previousSong = CurrentPlaylist.GetPreviousSong(_mainWindowViewModel.CurrentSong);

            if (previousSong == null)
            {
                Console.WriteLine("No previous song available.");
                return;
            }
            
            
            if (_mainWindowViewModel.SeekPosition > 3)
            {
                _audioPlayerService.Restart(_mainWindowViewModel.CurrentSong);
            }
            else
            {
                _mainWindowViewModel.CurrentSong = previousSong;
                _audioPlayerService.Start(previousSong.FilePath);
                IsPlaying = true;
            }
            _mainWindowViewModel.SeekPosition = 0;
        }


        private void MainWindowViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPlaylist))
            {
                CurrentPlaylist = _mainWindowViewModel.CurrentPlaylist;
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.CurrentSong))
            {
                // Optionally, update playback status or other properties
            }
        }

        private bool CanPreviousTrack() => CurrentPlaylist?.Songs.Count > 1;
    }
}
