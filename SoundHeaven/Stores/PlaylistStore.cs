using SoundHeaven.Services;
using SoundHeaven.ViewModels;
using System.Collections.ObjectModel;
using System.Linq;

namespace SoundHeaven.Models
{
    public class PlaylistStore : ViewModelBase
    {
        public ObservableCollection<Playlist> Playlists { get; set; }
        private int _currentPlaylistIndex = 0;
        private AudioPlayerService _audioPlayerService;

        public PlaylistStore(AudioPlayerService audioPlayerService)
        {
            Playlists = new ObservableCollection<Playlist>();
            _audioPlayerService = audioPlayerService;
        }

        public Playlist? GetCurrentPlaylist()
        {
            if (Playlists.Count > 0 && _currentPlaylistIndex >= 0 && _currentPlaylistIndex < Playlists.Count)
            {
                return Playlists[_currentPlaylistIndex];
            }
            return null;
        }

        public void PlayCurrentPlaylist()
        {
            var currentPlaylist = GetCurrentPlaylist();
            currentPlaylist?.PlayCurrentSong();
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

        public void SwitchToNextPlaylist()
        {
            if (_currentPlaylistIndex < Playlists.Count - 1)
            {
                _audioPlayerService.Stop(); // Stop any song playing in the current playlist
                _currentPlaylistIndex++;
                PlayCurrentPlaylist(); // Start playing the next playlist
            }
        }

        public void SwitchToPreviousPlaylist()
        {
            if (_currentPlaylistIndex > 0)
            {
                _audioPlayerService.Stop(); // Stop any song playing in the current playlist
                _currentPlaylistIndex--;
                PlayCurrentPlaylist(); // Start playing the previous playlist
            }
        }

        public void SwitchToPlaylist(int index)
        {
            if (index >= 0 && index < Playlists.Count)
            {
                _audioPlayerService.Stop(); // Stop the current playlist
                _currentPlaylistIndex = index;
                PlayCurrentPlaylist(); // Start playing the selected playlist
            }
        }

        public void SwitchToPlaylist(string playlistName)
        {
            var playlist = Playlists.FirstOrDefault(p => p.Name == playlistName);
            if (playlist != null)
            {
                _audioPlayerService.Stop(); // Stop the current playlist
                _currentPlaylistIndex = Playlists.IndexOf(playlist);
                PlayCurrentPlaylist(); // Start playing the selected playlist
            }
        }
    }
}
