using SoundHaven.Data;
using SoundHaven.Models;
using SoundHaven.ViewModels;
using System;
using System.Collections.ObjectModel;

namespace SoundHaven.Stores
{
    public class PlaylistStore : ViewModelBase
    {
        private readonly AppDatabase _appDatabase;
        private ObservableCollection<Playlist> _playlists;
        public ObservableCollection<Playlist> Playlists
        {
            get => _playlists;
        }

        public PlaylistStore(AppDatabase appDatabase)
        {
            _appDatabase = appDatabase;
            _playlists = new ObservableCollection<Playlist>(_appDatabase.GetAllPlaylists());
        }

        public void AddPlaylist(Playlist playlist)
        {
            _appDatabase.SavePlaylist(playlist);
            playlist.Id = (int)_appDatabase.GetPlaylistId(playlist.Name);
            Playlists.Add(playlist);
        }

        public void RemovePlaylist(Playlist playlist)
        {
            _appDatabase.RemovePlaylist(playlist);
            foreach (var song in playlist.Songs)
            {
                _appDatabase.RemoveSongFromPlaylist(playlist.Id, song.Id);
                Console.WriteLine($"Deleted song: {song.Title} from playlist: {playlist.Name}");
            }
            if (Playlists.Contains(playlist))
            {
                Playlists.Remove(playlist);
            }
        }
    }
}
