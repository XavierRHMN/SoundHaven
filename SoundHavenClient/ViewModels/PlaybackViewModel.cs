﻿using SoundHaven.Commands;
using SoundHaven.Helpers;
using SoundHaven.Models;
using SoundHaven.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SoundHaven.ViewModels
{
    public class PlaybackViewModel : ViewModelBase
    {
        private readonly ThemesViewModel _themesViewModel;
        private readonly ILastFmDataService _lastFmDataService;
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

                    Task.Run(ScrobbleCurrentSongAsync);
                    // Only run SetDynamicTheme if the dynamic theme is selected
                    if (_themesViewModel.IsDynamicThemeSelected) Task.Run(SetDynamicTheme);
                }
            }
        }
        
        private void SetDynamicTheme()
        {
            // Add a dynamic theme color based on the current dominant color of the album artwork
            if (CurrentSong != null)
            {
                var dominantColor = DominantColorFinder.GetDominantColor(CurrentSong.Artwork);
                _themesViewModel.ThemeColors[^1] = dominantColor;
                _themesViewModel.ChangeTheme(dominantColor);
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
        
        private bool _canPlaybackControl = true;
        public bool CanPlaybackControl
        {
            get => _canPlaybackControl;
            set => SetProperty(ref _canPlaybackControl, value);
        }

        public AsyncRelayCommand PlayCommand { get; set; }
        public AsyncRelayCommand PauseCommand { get; set; }
        public AsyncRelayCommand NextCommand { get; set; }
        public AsyncRelayCommand PreviousCommand { get; set; }

        public PlaybackViewModel(AudioService audioService, IYouTubeDownloadService youTubeDownloadService,
                                 RepeatViewModel repeatViewModel, LastFmLastFmDataService lastFmDataService, ThemesViewModel themesViewModel)
        {
            _themesViewModel = themesViewModel;
            _lastFmDataService = lastFmDataService;
            _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
            _youTubeDownloadService = youTubeDownloadService ?? throw new ArgumentNullException(nameof(youTubeDownloadService));
            _audioService.PlaybackStateChanged += OnPlaybackStateChanged;
            _audioService.TrackEnded += OnTrackEndedRobust;
            _repeatViewModel = repeatViewModel;

            InitializeCommands();
        }

        private void InitializeCommands()
        {
            PlayCommand = new AsyncRelayCommand(Play, CanPlay);
            PauseCommand = new AsyncRelayCommand(Pause, CanPause);
            NextCommand = new AsyncRelayCommand(NextTrack, CanNext);
            PreviousCommand = new AsyncRelayCommand(PreviousTrack, CanPrevious);
        }

        private bool CanPlay() => CurrentSongExists && !IsPlaying && CanPlaybackControl;

        private bool CanPause() => CurrentSongExists && IsPlaying;

        private bool CanNext() => CurrentSongExists && CanPlaybackControl;

        private bool CanPrevious() => CurrentSongExists && CanPlaybackControl;
        
        private async void ScrobbleCurrentSongAsync()
        {
            try
            { 
                await _lastFmDataService.ScrobbleTrackAsync(CurrentSong.Title!, CurrentSong.Artist!, CurrentSong.Album!);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scrobbling track: {ex}");
            }
        }
        
        public async Task Play()
        {
            if (CurrentSong != null)
            {
                if (_audioService.IsStopped)
                {
                    await PlayFromBeginning(CurrentSong);
                }
                else
                {
                    await _audioService.Resume();
                }
            }
            else if (CurrentPlaylist?.Songs.Count > 0)
            {
                CurrentSong = CurrentPlaylist.Songs[0];
                await PlayFromBeginning(CurrentSong);
            }
        }

        public async Task Pause()
        {
            if (_audioService.IsPlaying)
            {
                await _audioService.Pause();
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

            Song? nextSong;

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

            if (CurrentSong.IsYouTubeVideo)
            {
                _audioService.Restart();
            }
            else
            {
                // For local files, if we're before 5 seconds, try to go to the previous song
                if (_audioService.CurrentPosition.TotalSeconds < 5)
                {
                    var previousSong = CurrentPlaylist?.GetPreviousNextSong(CurrentSong, Direction.Previous);
                 
                    if (previousSong != null)
                    {
                        await PlayFromBeginning(previousSong);
                    }
                }
                else
                {
                    // If there's no previous song, restart the current one
                    _audioService.Restart();
                }   
            }
        }

        public async Task PlayFromBeginning(Song song)
        {
            try
            {
                bool isYouTubeVideo = song.IsYouTubeVideo;
                string? source = isYouTubeVideo ? song.VideoId : song.FilePath;

                CurrentSong = song;
                await _audioService.StartAsync(source, isYouTubeVideo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error playing from beginning: " + ex);
            }
        }

        public async Task AddToUpNext(Song song)
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

        private async void OnTrackEndedRobust(object sender, EventArgs e)
        {
            switch (_repeatViewModel.RepeatMode)
            {
                case RepeatMode.One:
                    await PlayFromBeginning(CurrentSong);
                    _repeatViewModel.SetRepeatModeOff();
                    break;
                case RepeatMode.All:
                    await PlayFromBeginning(CurrentSong);
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
                NextCommand.RaiseCanExecuteChanged();
                PreviousCommand.RaiseCanExecuteChanged();
            });
        }

        public override void Dispose()
        {
            _audioService.TrackEnded -= OnTrackEndedRobust;
            _audioService.PlaybackStateChanged -= OnPlaybackStateChanged;
        }
    }
}
