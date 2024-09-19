using SoundHeaven.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SoundHeaven.Stores
{
    public class PlaylistStore
    {
        
        // List to store all playlists
        private ObservableCollection<Playlist> _playlists;
        public ObservableCollection<Playlist> Playlists
        {
            get
            {
                return _playlists;
            }
        }


        // The currently active playlist
        public Playlist CurrentPlaylist { get; set; }

        // Constructor
        public PlaylistStore()
        {
            _playlists = new ObservableCollection<Playlist>();
        }

        // Adds a new playlist to the store
        public void AddPlaylist(Playlist playlist)
        {
            if (playlist == null)
                throw new ArgumentNullException(nameof(playlist));

            _playlists.Add(playlist);
        }

        // Removes a playlist by name
        public void RemovePlaylist(string playlistName)
        {
            var playlistToRemove = _playlists.FirstOrDefault(p => p.Name == playlistName);
            if (playlistToRemove != null)
            {
                _playlists.Remove(playlistToRemove);
            }
        }

        // Retrieves all playlists
        public ObservableCollection<Playlist> GetAllPlaylists()
        {
            return _playlists;
        }

        public string getName()
        {
            return CurrentPlaylist.Name;
        }

        // Finds a playlist by name
        public Playlist GetPlaylistByName(string playlistName)
        {
            return _playlists.FirstOrDefault(p => p.Name == playlistName);
        }

        // Sets the current playlist by name
        public void SetCurrentPlaylist(string playlistName)
        {
            var playlist = GetPlaylistByName(playlistName);
            if (playlist != null)
            {
                CurrentPlaylist = playlist;
            }
        }

        // Adds a song to a specific playlist
        public void AddSongToPlaylist(string playlistName, Song song)
        {
            var playlist = GetPlaylistByName(playlistName);
            if (playlist != null && song != null)
            {
                playlist.Songs.Add(song);
            }
        }

        // Removes a song from a specific playlist
        public void RemoveSongFromPlaylist(string playlistName, Song song)
        {
            var playlist = GetPlaylistByName(playlistName);
            if (playlist != null && song != null)
            {
                playlist.Songs.Remove(song);
            }
        }
    }
}
