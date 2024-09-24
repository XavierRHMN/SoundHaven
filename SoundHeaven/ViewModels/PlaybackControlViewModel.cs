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
    
    public class PlaybackControlViewModel : ViewModelBase, IPlaybackControlViewModel
    {
        public enum Direction
        {
            Previous = -1,
            Next = 1
        }

        private AudioService AudioService => _mainWindowViewModel.AudioService;
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

        public RelayCommand PlayCommand { get; private set; }
        public RelayCommand PauseCommand { get; private set; }
        public RelayCommand NextCommand { get; private set; }
        public RelayCommand PreviousCommand { get; private set; }

        public PlaybackControlViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;

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
                if (AudioService.IsStopped())
                {
                    AudioService.Start(song.FilePath);
                }
                else
                {
                    AudioService.Resume();
                }
                IsPlaying = true;
            }
            else if (_mainWindowViewModel.CurrentPlaylist?.Songs.Count > 0)
            {
                song = _mainWindowViewModel.CurrentPlaylist.Songs[0];
                _mainWindowViewModel.CurrentSong = song;
                AudioService.Start(song.FilePath);
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
            if (AudioService.IsPlaying())
            {
                AudioService.Pause();
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
                AudioService.Start(nextSong.FilePath);
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

            Song? previousSong = null;

            if (IsShuffleEnabled)
            {
                int index = Random.Shared.Next(currentPlaylist.Songs.Count);
                var currentSong = _mainWindowViewModel.CurrentSong;
                var songs = currentPlaylist.Songs;

                if (index == songs.IndexOf(currentSong))
                {
                    index = (index - 1 + songs.Count) % songs.Count;
                }
                previousSong = currentPlaylist.Songs[index];
            }
            else
            {
                previousSong = currentPlaylist.GetPreviousNextSong(_mainWindowViewModel.CurrentSong, Direction.Previous);
            }

            if (previousSong != null)
            {
                if (_mainWindowViewModel.SeekPosition > 3)
                {
                    AudioService.Restart(_mainWindowViewModel.CurrentSong);
                    _mainWindowViewModel.SeekPosition = 0;
                }
                else if (currentPlaylist.Songs.Count > 1)
                {
                    _mainWindowViewModel.CurrentSong = previousSong;
                    AudioService.Start(previousSong.FilePath);
                    IsPlaying = true;
                }
            }
            else
            {
                Console.WriteLine("No previous song available.");
            }
        }
    }
}
