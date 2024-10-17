using SoundHaven.Models;
using SoundHaven.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace SoundHaven.ViewModels
{
    public class HomeViewModel : ViewModelBase
    {
        private readonly PlaybackViewModel _playbackViewModel;
        private readonly IDataService _dataService;

        public ObservableCollection<Song> TopTracks { get; }
        public ObservableCollection<Song> RecentlyPlayedTracks { get; }
        public ObservableCollection<Song> RecommendedAlbums { get; }
        
        private bool _isLoading = true;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }
        
        private bool _isUsernamePromptVisible = true;
        public bool IsUsernamePromptVisible
        {
            get => _isUsernamePromptVisible;
            set => SetProperty(ref _isUsernamePromptVisible, value);
        }

        private string _username = string.Empty;
        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        public HomeViewModel(PlaybackViewModel playbackViewModel, IDataService dataService)
        {
            _playbackViewModel = playbackViewModel;
            _dataService = dataService;

            TopTracks = new ObservableCollection<Song>();
            RecentlyPlayedTracks = new ObservableCollection<Song>();
            RecommendedAlbums = new ObservableCollection<Song>();
        }
        
        public async Task SubmitUsernameAsync()
        {
            if (string.IsNullOrWhiteSpace(Username)) return;

            IsUsernamePromptVisible = false;
            await LoadDataAsync();
        }

        private async Task  LoadDataAsync()
        {
            IsLoading = true;
            string username = "NavFan";
            // TODO do something with this
            // var topTracks = await _dataService.GetTopTracksAsync();
            var recentlyPlayedTracks = await _dataService.GetRecentlyPlayedTracksAsync(username);
            var recommendedAlbums = await _dataService.GetRecommendedAlbumsAsync(username);

            // TopTracks.Clear();
            RecentlyPlayedTracks.Clear();
            RecommendedAlbums.Clear();

            // Shuffle using LINQ's OrderBy with a random key
            var shuffledAlbums = recommendedAlbums.OrderBy(track => new Random().Next()).ToList();

            foreach (var song in shuffledAlbums)
            {
                RecommendedAlbums.Add(song);
            }

            foreach (var song in recentlyPlayedTracks.Skip(1))
            {
                RecentlyPlayedTracks.Add(song);
            }

            IsLoading = false;

            // foreach (var song in topTracks)
            // {
            //     TopTracks.Add(song);
            // }
        }
    }
}
