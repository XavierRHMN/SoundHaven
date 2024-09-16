using System.Collections.ObjectModel;
using SoundHeaven.Models;

namespace SoundHeaven.ViewModels
{
    public class PlaylistViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainWindowViewModel;

        // Expose Playlists from MainWindowViewModel
        public ObservableCollection<Playlist> Playlists => _mainWindowViewModel.Playlists;

        private Playlist _currentPlaylist;
        public Playlist CurrentPlaylist
        {
            get => _currentPlaylist;
            set
            {
                if (_currentPlaylist != value)
                {
                    _currentPlaylist = value;
                    OnPropertyChanged();
                }
            }
        }

        private Song _currentSong;
        public Song CurrentSong
        {
            get => _currentSong;
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

        // Constructor accepting MainWindowViewModel
        public PlaylistViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;
        }

        // Load the songs from the selected playlist
        public void LoadPlaylistSongs(Playlist playlist)
        {
            CurrentPlaylist = playlist;

            // You can modify this section to load songs from the playlist
            // into a collection if necessary, or directly access playlist songs.
        }
    }
}
