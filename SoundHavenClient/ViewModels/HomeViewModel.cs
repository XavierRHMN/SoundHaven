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
        private readonly ILastFmDataService _lastFmDataService;

        public ObservableCollection<Song> RecentlyPlayedTracks { get; }
        public ObservableCollection<Song> RecommendedAlbums { get; }
        
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
        
        private string _password = string.Empty;
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        public HomeViewModel(ILastFmDataService lastFmDataService)
        {
            _lastFmDataService = lastFmDataService;

            RecentlyPlayedTracks = new ObservableCollection<Song>();
            RecommendedAlbums = new ObservableCollection<Song>();
        }
        
        public async Task SubmitUsernameAsync()
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password)) return;

            IsUsernamePromptVisible = false;
            await LoadDataAsync();
        }

        private async Task  LoadDataAsync()
        {
            IsLoading = true;
            // TODO do something with this
            // var topTracks = await _lastFmDataService.GetTopTracksAsync();
            _lastFmDataService.Username = Username;
            _lastFmDataService.Password = Password;
            var recentlyPlayedTracks = await _lastFmDataService.GetRecentlyPlayedTracksAsync();
            var recommendedAlbums = await _lastFmDataService.GetRecommendedAlbumsAsync();

            RecentlyPlayedTracks.Clear();
            RecommendedAlbums.Clear();

            // Shuffle using LINQ's OrderBy with a random key
            var shuffledAlbums = recommendedAlbums.OrderBy(_ => Guid.NewGuid()).ToList();

            await Task.WhenAll(
                Task.Run(() => {
                    foreach (var song in shuffledAlbums)
                    {
                        RecommendedAlbums.Add(song);
                    }
                }),
                Task.Run(() => {
                    foreach (var song in recentlyPlayedTracks)
                    {
                        RecentlyPlayedTracks.Add(song);
                    }
                })
            );

            IsLoading = false;
        }
    }
}
