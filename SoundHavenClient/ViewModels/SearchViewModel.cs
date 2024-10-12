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

namespace SoundHaven.ViewModels
{
    public class SearchViewModel : ViewModelBase
    {
        private readonly MpvDownloader _mpvDownloader;
        private readonly IYoutubeSearchService _youtubeSearchService;
        private readonly IYouTubeDownloadService _youTubeDownloadService;
        private readonly IOpenFileDialogService _openFileDialogService;
        private string _searchQuery;
        private ObservableCollection<Song> _searchResults;
        private bool _isLoading;
        private AudioService _audioService;
        private PlaybackViewModel _playbackViewModel;
        private bool _isMpvInitialized;

        
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


        public RelayCommand SearchCommand { get; }
        public RelayCommand<Song> PlaySongCommand { get; }
        public RelayCommand<Song> DownloadSongCommand { get; }
        public RelayCommand<Song> OpenFolderCommand { get; }

        public SearchViewModel(IYoutubeSearchService youtubeSearchService, IYouTubeDownloadService youTubeDownloadService,
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
            if (_isMpvInitialized) return;

            IsMpvLoading = true;
            LoadingMessage = "Initializing MPV...";

            try
            {
                await _mpvDownloader.DownloadAndUpdateMpvAsync();
                _isMpvInitialized = true;
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
                var results = await _youtubeSearchService.SearchVideos(SearchQuery);
                foreach (var result in results)
                {
                    var song = new Song
                    {
                        Title = result.Title,
                        Artist = result.ChannelTitle,
                        VideoId = result.VideoId,
                        ThumbnailUrl = result.ThumbnailUrl,
                        ChannelTitle = result.ChannelTitle,
                        Views = result.ViewCount,
                        VideoDuration = result.Duration,
                        Year = result.Year
                    };
                    await song.LoadYouTubeThumbnail();
                    SearchResults.Add(song);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during search: {ex.Message}");
            }
            IsLoading = false;
            LoadingMessage = string.Empty; 
        }

        private async void ExecutePlaySong(Song song)
        {
            try
            {
                bool isYouTubeVideo = song.IsYouTubeVideo;
                string? source = isYouTubeVideo ? CleanVideoId(song.VideoId) : song.FilePath;
                
                await _audioService.StartAsync(source, isYouTubeVideo);
                _playbackViewModel.CurrentSong = song;
                _playbackViewModel.CurrentPlaylist = new Playlist();
                _playbackViewModel.CurrentPlaylist.Name = "Streaming from YouTube";
                await _playbackViewModel.AddToUpNext(song); // Add this line
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing song: {ex.Message}");
            }
        }

        private string CleanVideoId(string videoId)
        {
            // Remove any parameters after '&'
            int ampersandIndex = videoId.IndexOf('&');
            if (ampersandIndex != -1)
            {
                videoId = videoId.Substring(0, ampersandIndex);
            }
            return videoId;
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

                var downloadedSong = await _youTubeDownloadService.DownloadAudioAsync(song.VideoId, progress);
                Console.WriteLine($"Song downloaded: {downloadedSong.Title} by {downloadedSong.Artist}");
                Console.WriteLine($"File path: {downloadedSong.FilePath}");

                // Update the original song in the search results with the downloaded information
                song.FilePath = downloadedSong.FilePath;
                song.Title = downloadedSong.Title;
                song.Artist = downloadedSong.Artist;
                song.CurrentDownloadState = DownloadState.Downloaded;
                // ... update other properties as needed
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading song: {ex.Message}");
                song.CurrentDownloadState = DownloadState.NotDownloaded;
            }
            finally
            {
                song.DownloadProgress = 0;
            }
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
