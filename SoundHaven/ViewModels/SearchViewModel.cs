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
        private readonly IYoutubeSearchService _youtubeSearchService;
        private readonly IYouTubeDownloadService _youTubeDownloadService;
        private readonly IOpenFileDialogService _openFileDialogService;
        private string _searchQuery;
        private ObservableCollection<Song> _searchResults;
        private bool _isLoading;

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

        public RelayCommand SearchCommand { get; }
        public RelayCommand<Song> PlaySongCommand { get; }
        public RelayCommand<Song> DownloadSongCommand { get; }
        public RelayCommand<Song> OpenFolderCommand { get; }

        public SearchViewModel(IYoutubeSearchService youtubeSearchService, IYouTubeDownloadService youTubeDownloadService, IOpenFileDialogService openFileDialogService)
        {
            _youtubeSearchService = youtubeSearchService;
            _youTubeDownloadService = youTubeDownloadService;
            _openFileDialogService = openFileDialogService;
            SearchResults = new ObservableCollection<Song>();

            SearchCommand = new RelayCommand(ExecuteSearch);
            PlaySongCommand = new RelayCommand<Song>(ExecutePlaySong);
            DownloadSongCommand = new RelayCommand<Song>(ExecuteDownloadSong);
            OpenFolderCommand = new RelayCommand<Song>(ExecuteOpenFolder);
        }

        private async void ExecuteSearch()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) return;

            IsLoading = true;
            SearchResults.Clear();

            try
            {
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
                        VideoDuration = result.Duration
                    };
                    await song.LoadYouTubeThumbnail();
                    SearchResults.Add(song);
                }
            }
            catch (Exception ex)
            {
                // Handle any exceptions that occur during the search
                Debug.WriteLine($"Error during search: {ex.Message}");
                // You might want to show an error message to the user here
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ExecutePlaySong(Song song)
        {
            // Implement the logic to play the selected song
            System.Diagnostics.Debug.WriteLine($"Playing song: {song.Title} by {song.Artist}");
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

                Song downloadedSong = await _youTubeDownloadService.DownloadAudioAsync(song.VideoId, progress);
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
            if (song == null || string.IsNullOrEmpty(song.FilePath)) return;

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
                // You might want to show an error message to the user here
            }
        }
    }
}