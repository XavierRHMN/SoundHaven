using SoundHeaven.Commands;
using SoundHeaven.Models;
using SoundHeaven.Services;
using System;
using System.ComponentModel;

namespace SoundHeaven.ViewModels
{
    public interface IPlaybackControlViewModel
    {
        public RelayCommand PlayCommand { get; }
        public RelayCommand PauseCommand { get; }
        public RelayCommand NextCommand { get; }
        public RelayCommand PreviousCommand { get; }
    }
    
    public class PlaybackViewModel : ViewModelBase, IPlaybackControlViewModel
    {
        public enum Direction
        {
            Previous = -1,
            Next = 1
        }


        private AudioService _audioService { get; set; }
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
                    OnPropertyChanged(nameof(IsShuffleEnabled));
                    Console.WriteLine($"PlaybackViewModel: Shuffle is now {(_isShuffleEnabled ? "enabled" : "disabled")}");
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

        public RelayCommand PlayCommand { get; private set; }
        public RelayCommand PauseCommand { get; private set; }
        public RelayCommand NextCommand { get; private set; }
        public RelayCommand PreviousCommand { get; private set; }

        public PlaybackViewModel(MainWindowViewModel mainWindowViewModel, AudioService audioService)
        {
            _mainWindowViewModel = mainWindowViewModel;
            _audioService = audioService;

            InitializeCommands();
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
                if (_audioService.IsStopped())
                {
                    _audioService.Start(song.FilePath);
                }
                else
                {
                    _audioService.Resume();
                }
                IsPlaying = true;
            }
            else if (_mainWindowViewModel.CurrentPlaylist?.Songs.Count > 0)
            {
                song = _mainWindowViewModel.CurrentPlaylist.Songs[0];
                _mainWindowViewModel.CurrentSong = song;
                _audioService.Start(song.FilePath);
                IsPlaying = true;
            }
            else
            {
                Console.WriteLine("No song or playlist available to play.");
                IsPlaying = false;
            }
        }

        private void Pause()
        {
            if (_audioService.IsPlaying())
            {
                _audioService.Pause();
                IsPlaying = false;
            }
        }

        private void NextTrack()
        {
            var currentPlaylist = _mainWindowViewModel.CurrentPlaylist;

            if (currentPlaylist == null || currentPlaylist.Songs.Count is 0 or 1)
            {
                Console.WriteLine("The playlist is empty or there's no next song");
                return;
            }

            Song? nextSong = null;

            if (IsShuffleEnabled)
            {
                int index = Random.Shared.Next(currentPlaylist.Songs.Count);
                var currentSong = _mainWindowViewModel.CurrentSong;
                var songs = currentPlaylist.Songs;

                if (index == songs.IndexOf(currentSong))
                {
                    index = (index + 1) % songs.Count;
                }
                nextSong = currentPlaylist.Songs[index];
            }
            else
            {
                nextSong = currentPlaylist.GetPreviousNextSong(_mainWindowViewModel.CurrentSong, Direction.Next);
            }

            if (nextSong != null)
            {
                _mainWindowViewModel.CurrentSong = nextSong;
                _audioService.Start(nextSong.FilePath);
                IsPlaying = true;
            }
            else
            {
                Console.WriteLine("No next song available.");
            }
        }

        private void PreviousTrack()
        {
            var currentPlaylist = _mainWindowViewModel.CurrentPlaylist;

            if (currentPlaylist == null || currentPlaylist.Songs.Count == 0)
            {
                Console.WriteLine("The playlist is empty.");
                return;
            }

            Song? previousSong = currentPlaylist.GetPreviousNextSong(_mainWindowViewModel.CurrentSong, Direction.Previous);
            
            if (previousSong != null)
            {
                if (_mainWindowViewModel.SeekSliderViewModel.SeekPosition > 3)
                {
                    _audioService.Restart(_mainWindowViewModel.CurrentSong);
                }
                else if (currentPlaylist.Songs.Count > 1)
                {
                    _mainWindowViewModel.CurrentSong = previousSong;
                    _audioService.Start(previousSong.FilePath);
                }
                IsPlaying = true;
            }
            else
            {
                Console.WriteLine("No previous song available.");
            }
        }
    }
}
