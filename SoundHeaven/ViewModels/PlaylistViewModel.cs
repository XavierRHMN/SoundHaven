using SoundHeaven.Models;
using SoundHeaven.Services;
using SoundHeaven.Stores;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SoundHeaven.ViewModels
{
    public class PlaylistViewModel : ViewModelBase
    {
        private MainWindowViewModel _mainWindowViewModel;
        private readonly PlaylistStore _playlistStore;

        public ObservableCollection<Playlist> Playlists => _playlistStore.Playlists;

        private Playlist _currentPlaylist;
        public Playlist CurrentPlaylist
        {
            get => _currentPlaylist;
            set
            {
                if (_currentPlaylist != value)
                {
                    if (_currentPlaylist != null)
                    {
                        _currentPlaylist.Songs.CollectionChanged -= OnSongsCollectionChanged;
                    }

                    _currentPlaylist = value;
                    OnPropertyChanged();

                    if (_currentPlaylist != null)
                    {
                        _currentPlaylist.Songs.CollectionChanged += OnSongsCollectionChanged;
                    }

                    OnPropertyChanged(nameof(PlaylistSongs));
                    _playlistStore.CurrentPlaylist = _currentPlaylist;
                }
            }
        }

        // Expose the songs of the current playlist
        public ObservableCollection<Song> PlaylistSongs => _mainWindowViewModel.CurrentPlaylist?.Songs;
        public string PlaylistName
        {
            get => _mainWindowViewModel.CurrentPlaylist.Name;
            set
            {
                if (_mainWindowViewModel.CurrentPlaylist.Name != value)
                {
                    _mainWindowViewModel.CurrentPlaylist.Name = value;
                    OnPropertyChanged();
                }
            }
        }

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

                    // Set the MainWindowViewModel's CurrentSong to play the song
                    _mainWindowViewModel.CurrentSong = _playlistCurrentSong;
                }
            }
        }
        
        // Constructor accepting MainWindowViewModel
        public PlaylistViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;
        }
        
        private void OnPlaylistStorePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlaylistStore.CurrentPlaylist))
            {
                OnPropertyChanged(nameof(PlaylistSongs));
            }
        }

        private void OnSongsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(PlaylistSongs));
        }
    }
}
