using SoundHaven.Commands;
using SoundHaven.Models;
using SoundHaven.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace SoundHaven.ViewModels
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

        private AudioService _audioService;

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

        public bool IsPlaying => _audioService.IsPlaying();

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
            _audioService.PlaybackStateChanged += OnPlaybackStateChanged;
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

        public void Play()
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
            }
            else if (CurrentPlaylist?.Songs.Count > 0)
            {
                CurrentSong = CurrentPlaylist.Songs[0];
                PlayFromBeginning(CurrentSong);
            }
            else
            {
                Console.WriteLine("No song or playlist available to play.");
            }
        }

        private void Pause()
        {
            if (_audioService.IsPlaying())
            {
                _audioService.Pause();
            }
        }

        public bool IsTransitioningTracks { get; set; }

        public void NextTrack()
        {
            if (CurrentPlaylist == null || CurrentPlaylist.Songs.Count is 0 or 1)
            {
                Console.WriteLine("The playlist is empty or there's no next song");
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
            }
            else
            {
                Console.WriteLine("No previous song available.");
            }
        }

        public async Task PlayFromBeginning(Song song)
        {
            if (song == null || string.IsNullOrEmpty(song.FilePath))
            {
                throw new ArgumentException("Invalid song or file path.");
            }

            await _audioService.StartAsync(song.FilePath);
        }
        
        public void AddToUpNext(Song song)
        {
            if (CurrentPlaylist == null)
            {
                CurrentPlaylist = new Playlist { Name = "Streaming from YouTube", Songs = new ObservableCollection<Song>() };
            }
    
            if (!CurrentPlaylist.Songs.Contains(song))
            {
                CurrentPlaylist.Songs.Add(song);
                OnPropertyChanged(nameof(CurrentPlaylist));
            }
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
                    NextTrack();
                    break;
            }
        }

        private void OnPlaybackStateChanged(object sender, EventArgs e)
        {
            OnPropertyChanged(nameof(IsPlaying));
            PlayCommand.RaiseCanExecuteChanged();
            PauseCommand.RaiseCanExecuteChanged();
        }

        public override void Dispose()
        {
            _audioService.TrackEnded -= OnTrackEndedRobust;
            _audioService.PlaybackStateChanged -= OnPlaybackStateChanged;
        }
    }
}