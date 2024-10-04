using SoundHaven.Data;
using SoundHaven.Models;
using SoundHaven.ViewModels;
using System;
using System.Collections.ObjectModel;

namespace SoundHaven.Stores
{
    public class PlaylistStore : ViewModelBase
    {
        private readonly MusicDatabase _musicDatabase;
        private ObservableCollection<Playlist> _playlists;
        public ObservableCollection<Playlist> Playlists
        {
            get
            {
                if (_playlists == null)
                {
                    LoadPlaylists();
                }
                return _playlists;
            }
        }

        public PlaylistStore(MusicDatabase musicDatabase)
        {
            _musicDatabase = musicDatabase;
            LoadPlaylists();
        }

        private void LoadPlaylists()
        {
            _playlists = new ObservableCollection<Playlist>(_musicDatabase.GetAllPlaylists());
        }

        public void AddPlaylist(Playlist playlist)
        {
            _musicDatabase.SavePlaylist(playlist);
            Playlists.Add(playlist);
        }

        public void RemovePlaylist(Playlist playlist)
        {
            _musicDatabase.RemovePlaylist(playlist);
            if (Playlists.Contains(playlist))
            {
                Playlists.Remove(playlist);
            }
        }
    }
}
