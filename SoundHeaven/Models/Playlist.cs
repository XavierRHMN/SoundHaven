using SoundHeaven.Services;
using System.Collections.ObjectModel;

namespace SoundHeaven.Models
{
    public class Playlist
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public ObservableCollection<Song> Songs { get; set; }

        private int _currentSongIndex = 0;
        private AudioPlayerService _audioPlayerService;

        public Playlist(AudioPlayerService audioPlayerService)
        {
            Songs = new ObservableCollection<Song>();
            _audioPlayerService = audioPlayerService;
        }

        public Song? GetCurrentSong()
        {
            if (Songs.Count > 0 && _currentSongIndex >= 0 && _currentSongIndex < Songs.Count)
            {
                return Songs[_currentSongIndex];
            }
            return null;
        }

        public void PlayCurrentSong()
        {
            var currentSong = GetCurrentSong();
            currentSong?.Play();
        }

        public void Next()
        {
            if (_currentSongIndex < Songs.Count - 1)
            {
                _audioPlayerService.Stop(); // Stop the current song
                _currentSongIndex++;
                PlayCurrentSong(); // Play the next song
            }
        }

        public void Previous()
        {
            if (_currentSongIndex > 0)
            {
                _audioPlayerService.Stop(); // Stop the current song
                _currentSongIndex--;
                PlayCurrentSong(); // Play the previous song
            }
        }
    }
}
