using SoundHeaven.Models;
using SoundHeaven.Stores;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SoundHeaven.ViewModels
{
    public class PlaylistViewModel : ViewModelBase
    {
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

        public ObservableCollection<Song> PlaylistSongs => _playlistStore.CurrentPlaylist?.Songs;

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

                    // Optionally, perform actions like playing the song
                }
            }
        }

        public PlaylistViewModel(PlaylistStore playlistStore)
        {
            _playlistStore = PlaylistStore.Instance;
            _playlistStore.PropertyChanged += OnPlaylistStorePropertyChanged;

            if (_currentPlaylist != null)
            {
                _currentPlaylist.Songs.CollectionChanged += OnSongsCollectionChanged;
            }
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
