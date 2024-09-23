using System;
using System.IO; // For regular file system operations
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SoundHeaven.Models;
using SoundHeaven.Services;
using System.Text.RegularExpressions;
using TagLib; // For TagLib operations

namespace SoundHeaven.Helpers
{
    public static class Mp3ToSongHelper
    {
        // Define the path where album covers will be saved
        private static readonly string CoversPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "Covers");

        // Extracts the album cover and saves it to the Assets/Covers directory
        public static void SaveAlbumCover(Song song)
        {
            if (song == null || string.IsNullOrWhiteSpace(song.FilePath))
                throw new ArgumentException("Invalid song or file path.");

            // Use System.IO.File for file system operations
            if (!System.IO.File.Exists(song.FilePath))
                throw new FileNotFoundException("MP3 file not found.", song.FilePath);

            // Use TagLib.File to read the MP3 metadata
            var file = TagLib.File.Create(song.FilePath);

            // Check if the artwork directory exists, if not, create it
            if (!Directory.Exists(CoversPath))
            {
                Directory.CreateDirectory(CoversPath);
            }

            // Extract and save artwork if available
            if (file.Tag.Pictures.Length > 0)
            {
                var picture = file.Tag.Pictures[0];
                using (var stream = new MemoryStream(picture.Data.Data))
                {
                    var artwork = new Bitmap(stream);

                    // Generate a unique file name for the artwork (e.g., based on song title or file name)
                    string? artworkFileName = $"{Path.GetFileNameWithoutExtension(song.FilePath)}_cover.png";
                    string? artworkFilePath = Path.Combine(CoversPath, artworkFileName);

                    // Use System.IO.File to save the artwork
                    // using (var fileStream = System.IO.File.Create(artworkFilePath))
                    // {
                    //     artwork.Save(fileStream);
                    // }

                    // Set the Image control's source to the saved artwork
                    song.Artwork = new Image { Source = new Bitmap(artworkFilePath) };
                }
            }
        }

        // Main method to get song metadata from an MP3 file
        public static Song GetSongFromMp3(string filePath)
        {
            // Use System.IO.File to check file existence
            if (!System.IO.File.Exists(filePath))
                throw new FileNotFoundException("MP3 file not found.", filePath);
            
            var song = new Song();
            // Use TagLib.File to read the MP3 metadata
            var file = TagLib.File.Create(filePath);

            // Extract basic metadata
            song.Title = CleanSongTitle(file.Tag.Title, string.Join(", ", file.Tag.Performers));
            song.Artist = string.Join(", ", file.Tag.Performers);
            song.Album = file.Tag.Album;
            song.Genre = string.Join(", ", file.Tag.Genres);
            song.Year = (int)file.Tag.Year;
            song.Duration = file.Properties.Duration;
            song.FilePath = filePath;

            // TODO - Fix this cause it throws exception
            SaveAlbumCover(song);

            return song;
        }

        // Method to clean the song title
        public static string CleanSongTitle(string title, string artist)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
                return title;

            // Regular expression to match patterns like "Artist - Title" or "Title by Artist"
            string pattern = $@"\b{Regex.Escape(artist)}\b\s*[-|by]*\s*|\s*[-|by]*\s*\b{Regex.Escape(artist)}\b";
            return Regex.Replace(title, pattern, "", RegexOptions.IgnoreCase).Trim();
        }
    }
}
