using SoundHaven.Models;
using SoundHaven.Services;
using SoundHaven.Commands;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SoundHaven.ViewModels
{
    public class HomeViewModel : ViewModelBase
    {
        private readonly PlaybackViewModel _playbackViewModel;
        private readonly IDataService _dataService;

        public ObservableCollection<Song> TopTracks { get; }
        public ObservableCollection<Song> RecentlyPlayedTracks { get; }
        public ObservableCollection<Song> RecommendedTracks { get; }
        
        private bool _isLoading = true;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public HomeViewModel(PlaybackViewModel playbackViewModel, IDataService dataService)
        {
            _playbackViewModel = playbackViewModel;
            _dataService = dataService;

            TopTracks = new ObservableCollection<Song>();
            RecentlyPlayedTracks = new ObservableCollection<Song>();
            RecommendedTracks = new ObservableCollection<Song>();

            LoadDataAsync();
        }

        private async void LoadDataAsync()
        {
            IsLoading = true;
            string? username = "NavFan";
            // TODO do something with this
            // var topTracks = await _dataService.GetTopTracksAsync();
            var recentlyPlayedTracks = await _dataService.GetRecentlyPlayedTracksAsync(username);
            var recommendedTracks = await _dataService.GetRecommendedTracksAsync(username);

            // TopTracks.Clear();
            RecentlyPlayedTracks.Clear();
            RecommendedTracks.Clear();

            // Shuffle using LINQ's OrderBy with a random key
            var shuffledTracks = recommendedTracks.OrderBy(track => new Random().Next()).ToList();

            RecommendedTracks.Clear();
            foreach (var song in shuffledTracks)
            {
                RecommendedTracks.Add(song);
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
