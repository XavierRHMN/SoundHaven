using System;
using System.Collections.ObjectModel;
using System.Linq;
using SoundHaven.Data;
using SoundHaven.Models;
using SoundHaven.ViewModels;

namespace SoundHaven.Stores
{
    public class PlaylistStore : ViewModelBase
    {
        private readonly AppDatabase _appDatabase;
        private readonly ObservableCollection<Playlist> _playlists;

        public ObservableCollection<Playlist> Playlists => _playlists;

        public PlaylistStore(AppDatabase appDatabase)
        {
            _appDatabase = appDatabase ?? throw new ArgumentNullException(nameof(appDatabase));
            _playlists = new ObservableCollection<Playlist>(_appDatabase.GetAllPlaylists());
        }

        public void AddPlaylist(Playlist playlist)
        {
            ArgumentNullException.ThrowIfNull(playlist);

            if (Playlists.Contains(playlist))
            {
                return;
            }

            _appDatabase.SavePlaylist(playlist);
            if (!Playlists.Any(existing => existing.Id == playlist.Id))
            {
                Playlists.Add(playlist);
            }
        }

        public void RemovePlaylist(Playlist playlist)
        {
            ArgumentNullException.ThrowIfNull(playlist);

            Playlist? storedPlaylist = Playlists.FirstOrDefault(existing =>
                ReferenceEquals(existing, playlist) ||
                (playlist.Id > 0 && existing.Id == playlist.Id));

            _appDatabase.RemovePlaylist(playlist);

            if (storedPlaylist != null)
            {
                Playlists.Remove(storedPlaylist);
                storedPlaylist.Id = 0;
            }

            playlist.Id = 0;
        }
    }
}
