using System.Collections.ObjectModel;
using SoundHeaven.Models;

namespace SoundHeaven.ViewModels
{
    public class HomeViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainWindowViewModel;

        private Song _currentSong;
        public Song CurrentSong
        {
            get
            {
                return _currentSong;
            }
            set
            {
                if (_currentSong != value)
                {
                    _currentSong = value;
                    OnPropertyChanged();

                    // Set the MainWindowViewModel's CurrentSong to the selected song from the playlist
                    _mainWindowViewModel.CurrentSong = _currentSong;
                }
            }
        }

        // Observable collection to hold recently played songs
        public ObservableCollection<Song> RecentlyPlayedSongs { get; }

        // Observable collection to hold recommended songs
        public ObservableCollection<Song> RecommendedSongs { get; }

        // Constructor accepting MainWindowViewModel
        public HomeViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;

            // Initialize collections
            RecentlyPlayedSongs = new ObservableCollection<Song>();
            RecommendedSongs = new ObservableCollection<Song>();

            // Load data into collections
            // TODO fix this
            // LoadData();
        }

        // Method to load sample data (replace with real data fetching)
        private void LoadData()
        {
// Sample data for recently played songs
// RecentlyPlayedSongs.Add(new Song { Title = "Song 1", Artist = "Artist A", Artwork = new Avalonia.Controls.Image { Source = new Avalonia.Media.Imaging.Bitmap("SoundHeaven/Assets/Covers/MissingAlbum.png") } });
// RecentlyPlayedSongs.Add(new Song { Title = "Song 2", Artist = "Artist B", Artwork = new Avalonia.Controls.Image { Source = new Avalonia.Media.Imaging.Bitmap("SoundHeaven/Assets/Covers/MissingAlbum.png") } });
//
// // Sample data for recommended songs
// RecommendedSongs.Add(new Song { Title = "Song 3", Artist = "Artist C", Artwork = new Avalonia.Controls.Image { Source = new Avalonia.Media.Imaging.Bitmap("SoundHeaven/Assets/Covers/MissingAlbum.png") } });
// RecommendedSongs.Add(new Song { Title = "Song 4", Artist = "Artist D", Artwork = new Avalonia.Controls.Image { Source = new Avalonia.Media.Imaging.Bitmap("SoundHeaven/Assets/Covers/MissingAlbum.png") } });
        }
    }
}
