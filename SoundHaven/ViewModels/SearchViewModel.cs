using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using SoundHaven.Commands;
using SoundHaven.Models;
using SoundHaven.Services;

namespace SoundHaven.ViewModels
{
    public class SearchViewModel : ViewModelBase
    {
        private readonly IYouTubeApiService _youTubeApiService;
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

        public ICommand SearchCommand { get; }
        public ICommand PlaySongCommand { get; }

        public SearchViewModel(IYouTubeApiService youTubeApiService)
        {
            _youTubeApiService = youTubeApiService;
            SearchResults = new ObservableCollection<Song>();

            SearchCommand = new RelayCommand(ExecuteSearch);
            PlaySongCommand = new RelayCommand<Song>(ExecutePlaySong);
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
            // This might involve updating the PlayerViewModel or using a service to handle playback
            System.Diagnostics.Debug.WriteLine($"Playing song: {song.Title} by {song.Artist}");
        }
    }
}
