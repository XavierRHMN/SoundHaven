using SoundHaven.Helpers;
using SoundHaven.Models;
using System.Collections.ObjectModel;
using System;
using System.IO;

namespace SoundHaven.Stores
{
    public class SongStore
    {
        // Collection of songs
        public ObservableCollection<Song> Songs { get; } = new ObservableCollection<Song>();
        
        // Add a song to the collection
        public void AddSong(Song? song)
        {
            if (song != null)
            {
                Songs.Add(song);
            }
        }

        // Remove a song from the collection
        public void RemoveSong(Song? song)
        {
            if (song != null)
            {
                Songs.Remove(song);
            }
        }
        
        // Load songs from the Tracks directory
        public void LoadSongs()
        {
            string projectDirectory = AppContext.BaseDirectory;
            string tracksPath = Path.Combine(projectDirectory, "..", "..", "..", "Tracks");

            if (!Directory.Exists(tracksPath))
            {
                Console.WriteLine("Tracks directory does not exist.");
                return;
            }

            string[] mp3Files = Directory.GetFiles(tracksPath, "*.mp3");
            Console.WriteLine($"Found {mp3Files.Length} MP3 files.");

            foreach (string mp3File in mp3Files)
            {
                try
                {
                    var song = Mp3ToSongHelper.GetSongFromMp3(mp3File);
                    AddSong(song);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading song from file {mp3File}: {ex.Message}");
                }
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
