// ViewModels/HomeViewModel.cs
using SoundHeaven.Commands;
using SoundHeaven.Models;
using SoundHeaven.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SoundHeaven.ViewModels
{
    public class HomeViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        private readonly IDataService _dataService;
        
        public ObservableCollection<Song> TopTracks { get;  }
        public ObservableCollection<Song> RecentlyPlayedTracks { get; }
        public ObservableCollection<Song> RecommendedTracks { get; }
        
        public Playlist? MainWindowViewModelCurrentPlaylist => _mainWindowViewModel.CurrentPlaylist;
        public ObservableCollection<Song>? Songs => MainWindowViewModelCurrentPlaylist?.Songs;

        
        private Song? _selectedSong;
        public Song? SelectedSong
        {
            get => _selectedSong;
            set
            {
                if (_selectedSong != value)
                {
                    _selectedSong = value;
                    OnPropertyChanged();

                    if (_selectedSong != null && _mainWindowViewModel.CurrentSong != _selectedSong)
                    {
                        _mainWindowViewModel.CurrentSong = _selectedSong;
                    }
                }
            }
        }
        public HomeViewModel(MainWindowViewModel mainWindowViewModel, IDataService dataService)
        {
            _mainWindowViewModel = mainWindowViewModel;
            _dataService = dataService;

            TopTracks = new ObservableCollection<Song>();
            RecentlyPlayedTracks = new ObservableCollection<Song>();
            RecommendedTracks = new ObservableCollection<Song>();
            
            LoadDataAsync();
        }

        private async void LoadDataAsync()
        {
            var username = "NavFan";
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
            
            // foreach (var song in topTracks)
            // {
            //     TopTracks.Add(song);
            // }
        }
    }
}
