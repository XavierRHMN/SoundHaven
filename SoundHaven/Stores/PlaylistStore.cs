using SoundHaven.Models;
using SoundHaven.Services;
using SoundHaven.ViewModels;
using System.Collections.ObjectModel;

namespace SoundHaven.Stores
{
    public class PlaylistStore : ViewModelBase
    {
        private ObservableCollection<Playlist>? _playlists;
        public ObservableCollection<Playlist> Playlists
        {
            get => _playlists;
            set => _playlists = value;
        }

        public PlaylistStore()
        {
            Playlists = new ObservableCollection<Playlist>();
        }

        public void AddPlaylist(Playlist playlist)
        {
            Playlists.Add(playlist);
        }

        public void RemovePlaylist(Playlist playlist)
        {
            if (Playlists.Contains(playlist))
            {
                Playlists.Remove(playlist);
            }
        }
    }
}
