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
        public ObservableCollection<Song> RecommendedTracks { get; }
        
        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading && !IsUsernamePromptVisible;
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
            RecommendedTracks = new ObservableCollection<Song>();
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
            // TODO do something with this
            // var topTracks = await _dataService.GetTopTracksAsync();
            var recentlyPlayedTracks = await _dataService.GetRecentlyPlayedTracksAsync(Username);
            var recommendedTracks = await _dataService.GetRecommendedTracksAsync(Username);

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
