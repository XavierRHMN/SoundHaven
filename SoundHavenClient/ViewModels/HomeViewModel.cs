using SoundHaven.Models;
using SoundHaven.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace SoundHaven.ViewModels
{
    public class HomeViewModel : ViewModelBase
    {
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

        public HomeViewModel(IDataService dataService)
        {
            _dataService = dataService;

            TopTracks = new ObservableCollection<Song>();
            RecentlyPlayedTracks = new ObservableCollection<Song>();
            RecommendedAlbums = new ObservableCollection<Song>();

            LoadDataAsync();
        }

        private async void LoadDataAsync()
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
