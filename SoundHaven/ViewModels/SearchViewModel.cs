using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using SoundHaven.Commands;
using SoundHaven.Models;
using SoundHaven.Services;
using System;

namespace SoundHaven.ViewModels
{
    public class SearchViewModel : ViewModelBase
    {
        private readonly IYouTubeApiService _youTubeApiService;
        private readonly IYouTubeDownloadService _youTubeDownloadService;
        private string _searchQuery;
        private ObservableCollection<Song> _searchResults;

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

        public RelayCommand SearchCommand { get; }
        public RelayCommand<Song> PlaySongCommand { get; }
        public RelayCommand<Song> DownloadSongCommand { get; }

        public SearchViewModel(IYouTubeApiService youTubeApiService, IYouTubeDownloadService youTubeDownloadService)
        {
            _youTubeApiService = youTubeApiService;
            _youTubeDownloadService = youTubeDownloadService;
            SearchResults = new ObservableCollection<Song>();

            SearchCommand = new RelayCommand(ExecuteSearch);
            PlaySongCommand = new RelayCommand<Song>(ExecutePlaySong);
            DownloadSongCommand = new RelayCommand<Song>(ExecuteDownloadSong);
        }

        private async void ExecuteSearch()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) return;

            var results = await _youTubeApiService.SearchVideos(SearchQuery);
            SearchResults.Clear();
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
                var progress = new Progress<double>(p => 
                {
                    // Update UI with download progress
                    Console.WriteLine($"Download progress: {p:P}");
                });

                Song downloadedSong = await _youTubeDownloadService.DownloadAudioAsync(song.VideoId, progress);
                Console.WriteLine($"Song downloaded: {downloadedSong.Title} by {downloadedSong.Artist}");
                Console.WriteLine($"File path: {downloadedSong.FilePath}");

                // You can now use the downloadedSong object to update your UI or add it to a playlist
                // For example:
                // AddToLibrary(downloadedSong);
                // or
                // UpdateUIWithDownloadedSong(downloadedSong);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading song: {ex.Message}");
            }
        }
    }
}