using SoundHeaven.Commands;
using SoundHeaven.Models;
using SoundHeaven.Services;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

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
        private readonly object _trackEndedLock = new object();
        private bool _isTrackEndedProcessing = false;
        private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(500);
        
        public enum Direction
        {
            Previous = -1,
            Next = 1
        }

        private AudioService _audioService { get; set; }

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

        private Song _currentSong;
        public Song CurrentSong
        {
            get => _currentSong;
            set
            {
                if (_currentSong != value)
                {
                    _currentSong = value;
                    OnPropertyChanged(nameof(CurrentSong));
                    OnPropertyChanged(nameof(CurrentSongExists));
                    if (value != null)
                    {
                        PlayFromBeginning(value);
                    }
                }
            }
        }

        public bool CurrentSongExists => CurrentSong != null;

        private Playlist _currentPlaylist;
        public Playlist CurrentPlaylist
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

        public PlaybackViewModel(AudioService audioService)
        {
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
            if (CurrentSong != null)
            {
                if (_audioService.IsStopped())
                {
                    PlayFromBeginning(CurrentSong);
                }
                else
                {
                    _audioService.Resume();
                }
                IsPlaying = true;
            }
            else if (CurrentPlaylist?.Songs.Count > 0)
            {
                CurrentSong = CurrentPlaylist.Songs[0];
                PlayFromBeginning(CurrentSong);
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
            if (CurrentPlaylist == null || CurrentPlaylist.Songs.Count is 0 or 1)
            {
                Console.WriteLine("The playlist is empty or there's no next song");
                return;
            }

            Song? nextSong = null;

            if (IsShuffleEnabled)
            {
                int index = Random.Shared.Next(CurrentPlaylist.Songs.Count);
                var songs = CurrentPlaylist.Songs;

                if (index == songs.IndexOf(CurrentSong))
                {
                    index = (index + 1) % songs.Count;
                }
                nextSong = CurrentPlaylist.Songs[index];
            }
            else
            {
                nextSong = CurrentPlaylist.GetPreviousNextSong(CurrentSong, Direction.Next);
            }

            if (nextSong != null)
            {
                CurrentSong = nextSong;
                PlayFromBeginning(nextSong);
                IsPlaying = true;
            }
            else
            {
                Console.WriteLine("No next song available.");
            }
        }

        private void PreviousTrack()
        {
            if (CurrentPlaylist == null || CurrentPlaylist.Songs.Count == 0)
            {
                Console.WriteLine("The playlist is empty.");
                return;
            }

            Song? previousSong = CurrentPlaylist.GetPreviousNextSong(CurrentSong, Direction.Previous);
            
            if (previousSong != null)
            {
                if (_audioService.GetCurrentTime().TotalSeconds > 3)
                {
                    PlayFromBeginning(CurrentSong);
                }
                else if (CurrentPlaylist.Songs.Count > 1)
                {
                    CurrentSong = previousSong;
                    PlayFromBeginning(previousSong);
                }
                IsPlaying = true;
            }
            else
            {
                Console.WriteLine("No previous song available.");
            }
        }

        private void PlayFromBeginning(Song song)
        {
            _audioService.Seek(TimeSpan.Zero);
            _audioService.Start(song.FilePath);
            IsPlaying = true;
        }

        private async Task HandleTrackEndedAsync()
        {
            lock (_trackEndedLock)
            {
                if (_isTrackEndedProcessing)
                {
                    return;
                }
                _isTrackEndedProcessing = true;
            }

            await Task.Delay(_debounceDelay);

            try
            {
                NextTrack();
            }
            finally
            {
                _isTrackEndedProcessing = false;
            }
        }

    }
}