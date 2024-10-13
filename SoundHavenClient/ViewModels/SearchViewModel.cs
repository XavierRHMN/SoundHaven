using Avalonia.Threading;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using SoundHaven.Commands;
using SoundHaven.Models;
using SoundHaven.Services;
using SoundHaven.Helpers;
using System.Collections.Generic;
using YoutubeExplode.Videos;

namespace SoundHaven.ViewModels
{
    public class SearchViewModel : ViewModelBase
    {
        private readonly MpvDownloader _mpvDownloader;
        private readonly IYouTubeSearchService _youtubeSearchService;
        private readonly IYouTubeDownloadService _youTubeDownloadService;
        private readonly IOpenFileDialogService _openFileDialogService;
        private string _searchQuery;
        private ObservableCollection<Song> _searchResults;
        private bool _isLoading;
        private AudioService _audioService;
        private PlaybackViewModel _playbackViewModel;
        
        public string SearchQuery
        {
            get => _searchQuery;
            set => SetProperty(ref _searchQuery, value);
        }

        public ObservableCollection<Song> SearchResults
        {
            get => _searchResults;
            set => SetProperty(ref _searchResults, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }
        
        private string _loadingMessage;
        public string LoadingMessage
        {
            get => _loadingMessage;
            set => SetProperty(ref _loadingMessage, value);
        }

        private Song _selectedSong;
        public Song SelectedSong
        {
            get => _selectedSong;
            set
            {
                if (SetProperty(ref _selectedSong, value))
                {
                    PlaySongCommand.Execute(value);
                }
            }
        }
        
        private bool _isMpvLoading;
        public bool IsMpvLoading
        {
            get => _isMpvLoading;
            set => SetProperty(ref _isMpvLoading, value);
        }
        
        private bool _isScrollViewerHittestable = true;
        public bool IsScrollViewerHittestable
        {
            get => _isScrollViewerHittestable;
            set => SetProperty(ref _isScrollViewerHittestable, value);
        }
        
        public RelayCommand SearchCommand { get; }
        public RelayCommand<Song> PlaySongCommand { get; }
        public RelayCommand<Song> DownloadSongCommand { get; }
        public RelayCommand<Song> OpenFolderCommand { get; }

        public SearchViewModel(IYouTubeSearchService youtubeSearchService, IYouTubeDownloadService youTubeDownloadService,
                               IOpenFileDialogService openFileDialogService, AudioService audioService, PlaybackViewModel playbackViewModel, MpvDownloader mpvDownloader)
        {
            _youtubeSearchService = youtubeSearchService;
            _youTubeDownloadService = youTubeDownloadService;
            _openFileDialogService = openFileDialogService;
            _audioService = audioService;
            _playbackViewModel = playbackViewModel;
            _mpvDownloader = mpvDownloader;
            
            SearchResults = new ObservableCollection<Song>();

            Task.Run(InitializeMpvAsync);
            SearchCommand = new RelayCommand(ExecuteSearch);
            PlaySongCommand = new RelayCommand<Song>(ExecutePlaySong);
            DownloadSongCommand = new RelayCommand<Song>(ExecuteDownloadSong);
            OpenFolderCommand = new RelayCommand<Song>(ExecuteOpenFolder);
        }

        private async Task InitializeMpvAsync()
        {
            IsMpvLoading = true;
            LoadingMessage = "Initializing MPV...";

            try
            {
                await _mpvDownloader.DownloadAndUpdateMpvAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing MPV: {ex.Message}");
            }
            IsMpvLoading = false;
            LoadingMessage = string.Empty;
        }
        
        private async void ExecuteSearch()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) return;

            IsLoading = true;
            SearchResults.Clear();

            try
            {
                LoadingMessage = "Loading songs...";
                var results = await _youtubeSearchService.SearchVideosAsync(SearchQuery, 15);
                
                IsLoading = false;
                LoadingMessage = string.Empty;

                await AddResultsGradually(results);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during search: {ex.Message}");
                IsLoading = false;
                LoadingMessage = string.Empty;
            }
        }

        private async Task AddResultsGradually(IEnumerable<VideoSearchResult> results)
        {
            foreach (var result in results)
            {
                var song = new Song
                {
                    Title = result.Title,
                    Artist = result.Author,
                    VideoId = result.VideoId,
                    ThumbnailUrl = result.ThumbnailUrl,
                    ChannelTitle = result.Author,
                    VideoDuration = FormatDurationToMinutesSeconds(result.Duration),
                    Views = FormatViewCount(result.ViewCount),
                    Year = result.Year
                };

                await song.LoadYouTubeThumbnail();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SearchResults.Add(song);
                });
            }
        }
        
        private string FormatDurationToMinutesSeconds(TimeSpan? duration)
        {
            if (!duration.HasValue)
                return "0:00";

            int hours = duration.Value.Hours;
            int minutes = duration.Value.Minutes;
            int seconds = duration.Value.Seconds;

            if (hours > 0)
                return $"{hours}:{minutes:D2}:{seconds:D2}";
            else
                return $"{minutes}:{seconds:D2}";
        }
        
        private string FormatViewCount(long viewCount)
        {
            if (viewCount < 1000)
                return $"{viewCount} views";
            else if (viewCount < 1_000_000)
                return $"{viewCount / 1000.0:F1}K views".Replace(".0K", "K");
            else
                return $"{viewCount / 1_000_000.0:F1}M views".Replace(".0M", "M");
        }

        private async void ExecutePlaySong(Song song)
        {
            try
            {
                IsScrollViewerHittestable = false;
                bool isYouTubeVideo = song.IsYouTubeVideo;
                string? source = isYouTubeVideo ? song.VideoId : song.FilePath;
                
                _playbackViewModel.CurrentSong = song;
                _playbackViewModel.CurrentPlaylist = new Playlist();
                _playbackViewModel.CurrentPlaylist.Name = "Streaming from YouTube";
                await _playbackViewModel.AddToUpNext(song); 
                await _playbackViewModel.PlayFromBeginning(song);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing song: {ex.Message}");
            }
            
            IsScrollViewerHittestable = true;
        }

        private async void ExecuteDownloadSong(Song song)
        {
            if (song == null) return;

            try
            {
                song.CurrentDownloadState = DownloadState.Downloading;
                var progress = new Progress<double>(p =>
                {
                    song.DownloadProgress = p * 100; // Convert to percentage
                });

                string cleanVideoId = _youTubeDownloadService.CleanVideoId(song.VideoId);
                var downloadedSong = await _youTubeDownloadService.DownloadAudioAsync(cleanVideoId, progress);
                Console.WriteLine($"Song downloaded: {downloadedSong.Title} by {downloadedSong.Artist}");
                Console.WriteLine($"File path: {downloadedSong.FilePath}");

                // Update the original song in the search results with the downloaded information
                song.FilePath = downloadedSong.FilePath;
                song.Title = downloadedSong.Title;
                song.Artist = downloadedSong.Artist;
                song.CurrentDownloadState = DownloadState.Downloaded;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading song: {ex.Message}");
                song.CurrentDownloadState = DownloadState.NotDownloaded;
            }
            song.DownloadProgress = 0;
        }

        private void ExecuteOpenFolder(Song song)
        {
            try
            {
                string folder = Path.GetDirectoryName(song.FilePath);
                if (folder != null)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = folder,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening folder: {ex.Message}");
            }
        }
    }
}
