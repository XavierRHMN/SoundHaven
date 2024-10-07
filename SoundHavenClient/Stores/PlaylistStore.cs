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
            get => _playlists;
        }

        public PlaylistStore(MusicDatabase musicDatabase)
        {
            _musicDatabase = musicDatabase;
            _playlists = new ObservableCollection<Playlist>(_musicDatabase.GetAllPlaylists());
        }

        public void AddPlaylist(Playlist playlist)
        {
            _musicDatabase.SavePlaylist(playlist);
            playlist.Id = (int)_musicDatabase.GetPlaylistId(playlist.Name);
            Playlists.Add(playlist);
        }

        public void RemovePlaylist(Playlist playlist)
        {
            _musicDatabase.RemovePlaylist(playlist);
            foreach (var song in playlist.Songs)
            {
                _musicDatabase.RemoveSongFromPlaylist(playlist.Id, song.Id);
                Console.WriteLine($"Deleted song: {song.Title} from playlist: {playlist.Name}");
            }
            if (Playlists.Contains(playlist))
            {
                Playlists.Remove(playlist);
            }
        }
    }
}
