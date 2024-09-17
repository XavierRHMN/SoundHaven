using System.Collections.ObjectModel;
using SoundHeaven.Models;

namespace SoundHeaven.ViewModels
{
    public class PlaylistViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        public MainWindowViewModel MainWindowViewModel { get; set; }

        // Expose the SongCollection from MainWindowViewModel
        public ObservableCollection<Song> PlaylistSongs => _mainWindowViewModel.SongCollection;

        private Song _playlistCurrentSong;
        public Song PlaylistCurrentSong
        {
            get => _playlistCurrentSong;
            set
            {
                if (_playlistCurrentSong != value)
                {
                    _playlistCurrentSong = value;
                    OnPropertyChanged();

                    // Set the MainWindowViewModel's CurrentSong to the selected song
                    _mainWindowViewModel.CurrentSong = _playlistCurrentSong;
                }
            }
        }
        
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

        // Constructor that accepts MainWindowViewModel
        public PlaylistViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;

            // Subscribe to CurrentSong property changes in MainWindowViewModel
            _mainWindowViewModel.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.CurrentSong))
                {
                    // Update PlaylistCurrentSong when MainWindowViewModel's CurrentSong changes
                    PlaylistCurrentSong = _mainWindowViewModel.CurrentSong;
                }
            };
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
