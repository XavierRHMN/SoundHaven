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
    public class PlaybackViewModel : ViewModelBase
    {
        private RepeatViewModel _repeatViewModel;
        private readonly IYouTubeDownloadService _youTubeDownloadService;
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
            set => SetProperty(ref _isShuffleEnabled, value);
        }

        public bool IsPlaying
        {
            get => _audioService.IsPlaying;
        }

        private Song _currentSong;
        public Song CurrentSong
        {
            get => _currentSong;
            set
            {
                if (SetProperty(ref _currentSong, value))
                {
                    OnPropertyChanged(nameof(CurrentSongExists));
                    PlayCommand.RaiseCanExecuteChanged();
                    PauseCommand.RaiseCanExecuteChanged();

                    Console.WriteLine("Started playing: " + _currentSong.Title + " - " + _currentSong.Artist + " - " + _currentSong.Year);

                    SeekPositionReset?.Invoke(this, EventArgs.Empty);
                    
                    if (!_currentSong.IsYouTubeVideo) PlayFromBeginning(value);
                }
            }
        }

        public bool CurrentSongExists
        {
            get => CurrentSong != null;
        }

        private Playlist _currentPlaylist;
        public Playlist CurrentPlaylist
        {
            get => _currentPlaylist;
            set => SetProperty(ref _currentPlaylist, value);
        }

        public RelayCommand PlayCommand { get; set; }
        public RelayCommand PauseCommand { get; set; }
        public AsyncRelayCommand NextCommand { get; set; }
        public AsyncRelayCommand PreviousCommand { get; set; }

        public PlaybackViewModel(AudioService audioService, IYouTubeDownloadService youTubeDownloadService, RepeatViewModel repeatViewModel)
        {
            _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
            _youTubeDownloadService = youTubeDownloadService ?? throw new ArgumentNullException(nameof(youTubeDownloadService));
            _audioService.PlaybackStateChanged += OnPlaybackStateChanged;
            _audioService.TrackEnded += OnTrackEndedRobust;
            _repeatViewModel = repeatViewModel;

            InitializeCommands();
        }

        private void InitializeCommands()
        {
            PlayCommand = new RelayCommand(Play, CanPlay);
            PauseCommand = new RelayCommand(Pause, CanPause);
            NextCommand = new AsyncRelayCommand(NextTrack);
            PreviousCommand = new AsyncRelayCommand(PreviousTrack);
        }

        private bool CanPlay() => CurrentSong != null && !IsPlaying;

        private bool CanPause() => CurrentSong != null && IsPlaying;

        public async void Play()
        {
            if (CurrentSong != null)
            {
                if (_audioService.IsStopped)
                {
                    await PlayFromBeginning(CurrentSong);
                }
                else
                {
                    _audioService.Resume();
                }
            }
            else if (CurrentPlaylist?.Songs.Count > 0)
            {
                CurrentSong = CurrentPlaylist.Songs[0];
                await PlayFromBeginning(CurrentSong);
            }
            else
            {
                Console.WriteLine("No song or playlist available to play.");
            }
        }

        public void Pause()
        {
            if (_audioService.IsPlaying)
            {
                _audioService.Pause();
            }
        }

        public bool IsTransitioningTracks { get; set; }

        public async Task NextTrack()
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
                await PlayFromBeginning(nextSong);
            }
            else
            {
                Console.WriteLine("No next song available.");
            }
        }

        public async Task PreviousTrack()
        {
            if (CurrentSong == null)
            {
                Console.WriteLine("No current song to restart or go back from.");
                return;
            }

            if (CurrentSong.IsYouTubeVideo || _audioService.CurrentLocalPosition.TotalSeconds > 5)
            {
                // For YouTube videos or if we're outside the first 3 seconds of any song, always restart
                await PlayFromBeginning(CurrentSong);
            }
            else
            {
                // For local files, if we're past 3 seconds, try to go to the previous song
                var previousSong = CurrentPlaylist?.GetPreviousNextSong(CurrentSong, Direction.Previous);

                if (previousSong != null)
                {
                    await PlayFromBeginning(previousSong);
                }
                else
                {
                    // If there's no previous song, restart the current one
                    await PlayFromBeginning(CurrentSong);
                }
            }
        }

        public async Task PlayFromBeginning(Song song)
        {
            if (song == null)
            {
                throw new ArgumentException("Invalid song.");
            }

            bool isYouTubeVideo = !string.IsNullOrEmpty(song.VideoId);
            string? source = isYouTubeVideo ? song.VideoId : song.FilePath;

            if (string.IsNullOrEmpty(source))
            {
                throw new ArgumentException("Missing file path/YouTube ID.");
            }

            if (isYouTubeVideo)
            {
                source = _youTubeDownloadService.CleanVideoId(source);
            }

            await _audioService.StartAsync(source, isYouTubeVideo);
            CurrentSong = song;
        }

        public async Task AddToUpNext(Song song)
        {
            if (CurrentPlaylist == null)
            {
                CurrentPlaylist = new Playlist { Name = "Streaming from YouTube", Songs = new ObservableCollection<Song>() };
            }

            if (!string.IsNullOrEmpty(song.VideoId))
            {
                // Clean the video ID
                song.VideoId = _youTubeDownloadService.CleanVideoId(song.VideoId);
            }

            if (!CurrentPlaylist.Songs.Contains(song))
            {
                CurrentPlaylist.Songs.Add(song);
                OnPropertyChanged(nameof(CurrentPlaylist));

                // If this is the first song added and no song is currently playing, start playing it
                if (CurrentPlaylist.Songs.Count == 1 && CurrentSong == null)
                {
                    await PlayFromBeginning(song);
                }
            }
        }

        private async void OnTrackEndedRobust(object sender, EventArgs e)
        {
            switch (_repeatViewModel.RepeatMode)
            {
                case RepeatMode.One:
                    await PreviousTrack();
                    _repeatViewModel.SetRepeatModeOff();
                    break;
                case RepeatMode.All:
                    await PreviousTrack();
                    break;
                case RepeatMode.Off:
                    await NextTrack();
                    break;
            }
        }

        private void OnPlaybackStateChanged(object sender, EventArgs e)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(IsPlaying));
                PlayCommand.RaiseCanExecuteChanged();
                PauseCommand.RaiseCanExecuteChanged();
            });
        }

        public override void Dispose()
        {
            _audioService.TrackEnded -= OnTrackEndedRobust;
            _audioService.PlaybackStateChanged -= OnPlaybackStateChanged;
        }
    }
}
