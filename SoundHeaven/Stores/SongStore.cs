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
        private ObservableCollection<Song> _songs;
        public ObservableCollection<Song> Songs => _songs;

        public SongStore()
        {
            _songs = new ObservableCollection<Song>();
        }
        
        public void AddSong(Song song)
        {
            if (song != null)
            {
                _songs.Add(song);
            }
        }
        
        public void RemoveSong(Song song)
        {
            if (song != null)
            {
                _songs.Remove(song);
            }
        }
        
        // Load initial data (optional)
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

                // Iterate over each MP3 file
                foreach (var mp3File in mp3Files)
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
    }
}
