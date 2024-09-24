// ViewModels/HomeViewModel.cs
using SoundHeaven.Commands;
using SoundHeaven.Models;
using SoundHeaven.Services;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SoundHeaven.ViewModels
{
    public class HomeViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        private readonly IDataService _dataService;
        public PlaybackControlViewModel PlaybackControlViewModel => _mainWindowViewModel.PlaybackControlViewModel;
        
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
        
        
        public ICommand PlayCommand => PlaybackControlViewModel.PlayCommand;
        public ICommand PauseCommand => PlaybackControlViewModel.PauseCommand;
        public ICommand NextCommand => PlaybackControlViewModel.NextCommand;
        public ICommand PreviousCommand => PlaybackControlViewModel.PreviousCommand;

        public ICommand PlaySongCommand { get; }
        
        public HomeViewModel(MainWindowViewModel mainWindowViewModel, IDataService dataService)
        {
            _mainWindowViewModel = mainWindowViewModel;
            _dataService = dataService;

            TopTracks = new ObservableCollection<Song>();
            RecentlyPlayedTracks = new ObservableCollection<Song>();
            RecommendedTracks = new ObservableCollection<Song>();
            
            PlaySongCommand = new RelayCommand<Song>(PlaySong);
            
            LoadDataAsync();
        }

        private async void LoadDataAsync()
        {
            var username = "NavFan";
            var topTracks = await _dataService.GetTopTracksAsync();
            var recentlyPlayedTracks = await _dataService.GetRecentlyPlayedTracksAsync(username);
            var recommendedTracks = await _dataService.GetRecommendedTracksAsync();
            
            TopTracks.Clear();
            RecentlyPlayedTracks.Clear();
            RecommendedTracks.Clear();

            foreach (var song in topTracks)
            {
                TopTracks.Add(song);
            }
            
            foreach (var song in recentlyPlayedTracks)
            {
                RecentlyPlayedTracks.Add(song);
            }
            
            foreach (var song in recommendedTracks)
            {
                RecommendedTracks.Add(song);
            }
        }
        
        private void PlaySong(Song song)
        {
            if (song != null)
            {
                _mainWindowViewModel.CurrentSong = song;
                PlaybackControlViewModel.PlayCommand.Execute(null);
            }
        }
    }
}
