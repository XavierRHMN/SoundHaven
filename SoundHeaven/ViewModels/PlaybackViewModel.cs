using SoundHeaven.Commands;
using SoundHeaven.Models;
using SoundHeaven.Services;
using System;
using System.ComponentModel;
using System.Threading;
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
        private RepeatViewModel _repeatViewModel;
        public event EventHandler SeekPositionReset;
        
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
                    PlayCommand.RaiseCanExecuteChanged();
                    PauseCommand.RaiseCanExecuteChanged();
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
                    PlayCommand.RaiseCanExecuteChanged();
                    PauseCommand.RaiseCanExecuteChanged();
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

        public RelayCommand PlayCommand { get; set; }
        public RelayCommand PauseCommand { get; set; }
        public RelayCommand NextCommand { get; set; }
        public RelayCommand PreviousCommand { get; set; }

        public PlaybackViewModel(AudioService audioService, RepeatViewModel repeatViewModel)
        {
            _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
            _audioService.TrackEnded += OnTrackEndedRobust;
            _repeatViewModel = repeatViewModel;

            InitializeCommands();
        }

        private void InitializeCommands()
        {
            PlayCommand = new RelayCommand(Play, CanPlay);
            PauseCommand = new RelayCommand(Pause, CanPause);
            NextCommand = new RelayCommand(NextTrack);
            PreviousCommand = new RelayCommand(PreviousTrack);
        }

        private bool CanPlay()
        {
            return CurrentSong != null && !IsPlaying;
        }

        private bool CanPause()
        {
            return CurrentSong != null && IsPlaying;
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

        public bool IsTransitioningTracks { get; set; }

        public void NextTrack()
        {
            if (CurrentPlaylist == null || CurrentPlaylist.Songs.Count is 0 or 1)
            {
                Console.WriteLine("The playlist is empty or there's no next song");
                IsPlaying = false;
                return;
            }
    
            Song? nextSong = null;
            
            SeekPositionReset?.Invoke(this, EventArgs.Empty);


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
                IsPlaying = false;
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
            _audioService.Start(song.FilePath);
            IsPlaying = true;
        }

        private void OnTrackEndedRobust(object sender, EventArgs e)
        {
            switch (_repeatViewModel.RepeatMode)
            {
                case RepeatMode.One:
                    PreviousTrack();
                    _repeatViewModel.SetRepeatModeOff();
                    break;
                case RepeatMode.All:
                    PreviousTrack();
                    break;
                case RepeatMode.Off:
                    // Do nothing, just move to the next track
                    NextTrack();
                    break;
            }
        }
        
        public override void Dispose()
        {
            _audioService.TrackEnded -= OnTrackEndedRobust;
        }
    }
}