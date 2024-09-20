using SoundHeaven.Commands;
using SoundHeaven.Models;
using SoundHeaven.Services;
using System;

namespace SoundHeaven.ViewModels
{
    public class PlaybackControlViewModel : ViewModelBase
    {
        private readonly AudioPlayerService _audioPlayerService;
        private readonly MainWindowViewModel _mainWindowViewModel;
        private readonly PlaylistViewModel _playlistViewModel;

        // Playback state
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

        private Playlist _currentPlaylist => _mainWindowViewModel.PlaylistViewModel.CurrentPlaylist;

        // Commands for controlling playback
        public RelayCommand PlayCommand { get; }
        public RelayCommand PauseCommand { get; }
        public RelayCommand NextCommand { get; }
        public RelayCommand PreviousCommand { get; }

        // Constructor
        public PlaybackControlViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _audioPlayerService = mainWindowViewModel.AudioService;
            _mainWindowViewModel = mainWindowViewModel;

            // Define commands
            PlayCommand = new RelayCommand(Play);
            PauseCommand = new RelayCommand(Pause);
            NextCommand = new RelayCommand(NextTrack);
            PreviousCommand = new RelayCommand(PreviousTrack);

            // Set initial state based on whether audio is playing
            IsPlaying = true;
        }

        // Method to play music
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
            else
            {
                Console.WriteLine("No song selected to play.");
            }
        }


        private bool CanPlay() => !_audioPlayerService.IsStopped();

        // Method to pause music
        private void Pause()
        {
            var song = _mainWindowViewModel.CurrentSong;
            if (song != null)
            {
                _audioPlayerService.Pause();
                IsPlaying = false;
            }
        }

        private bool CanPause() => !_audioPlayerService.IsStopped();

        // Method to go to the next track
        private void NextTrack()
        {
            // Implement logic for next track if available
            Console.WriteLine("Next track command invoked.");
            // Example: Switch to the next song in the playlist
        }

        private bool CanNextTrack() =>
            // Implement logic to determine if next track is available
            false; // Placeholder

        // Method to go to the previous track
        private void PreviousTrack()
        {
            // Implement logic for previous track if available
            Console.WriteLine("Previous track command invoked.");
            // Example: Switch to the previous song in the playlist
        }

        private bool CanPreviousTrack() =>
            // Implement logic to determine if previous track is available
            false; // Placeholder
    }
}
