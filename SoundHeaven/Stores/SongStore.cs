using SoundHeaven.Helpers;
using System.Collections.ObjectModel;
using SoundHeaven.Models;
using System;
using System.IO;

namespace SoundHeaven.Stores
{
    public class SongStore
    {
        // Collection of songs
        private ObservableCollection<Song> _songs = new ObservableCollection<Song>();
        public ObservableCollection<Song> Songs => _songs;
        
        // Add a song to the collection
        public void AddSong(Song song)
        {
            if (song != null)
            {
                _songs.Add(song);
            }
        }

        // Remove a song from the collection
        public void RemoveSong(Song song)
        {
            if (song != null)
            {
                _songs.Remove(song);
            }
        }

        // Current song index
        private int _currentSongIndex = 0;

        // Get the current song based on the index
        public Song CurrentSong => _songs[_currentSongIndex];

        // Can go to next song if there are more than one song
        public bool CanNext => _songs.Count > 1;

        // Can go to previous song if there are more than one song
        public bool CanPrevious => _songs.Count > 1;

        // Navigate to the next song
        public Song NextSong()
        {
            if (_songs.Count == 0)
                return null;

            _currentSongIndex = (_currentSongIndex + 1) % _songs.Count;
            return CurrentSong;
        }

        // Navigate to the previous song
        public Song PreviousSong()
        {
            if (_songs.Count == 0)
                return null;

            _currentSongIndex = (_currentSongIndex - 1 + _songs.Count) % _songs.Count;
            return CurrentSong;
        }

        public ObservableCollection<Song> GetAllSongs() => _songs;

        // Load songs from the Tracks directory
        public void LoadSongs()
        {
            // Get the base directory of the executable
            string projectDirectory = AppContext.BaseDirectory;
            string tracksPath = Path.Combine(projectDirectory, "..", "..", "..", "Tracks");

            // Ensure the directory exists
            if (Directory.Exists(tracksPath))
            {
                // Get all MP3 files in the directory
                string[] mp3Files = Directory.GetFiles(tracksPath, "*.mp3");
                
                Console.WriteLine(mp3Files.Length);

                // Iterate over each MP3 file
                foreach (string? mp3File in mp3Files)
                {
                    try
                    {
                        // Convert the MP3 file to a Song object
                        var song = Mp3ToSongHelper.GetSongFromMp3(mp3File);
                        
                        // Add the song to the SongStore
                        AddSong(song);
                    }
                    catch (Exception ex)
                    {
                        // Handle any potential exceptions (e.g., invalid MP3 file)
                        Console.WriteLine($"Error loading song from file {mp3File}: {ex.Message}");
                    }
                }
            }
            else
            {
                Console.WriteLine("Tracks directory does not exist.");
            }
        }

        // Save album covers for all songs
        public void SaveAlbumCovers()
        {
            foreach (var song in Songs)
            {
                Mp3ToSongHelper.SaveAlbumCover(song);
            }
        }
    }
}
