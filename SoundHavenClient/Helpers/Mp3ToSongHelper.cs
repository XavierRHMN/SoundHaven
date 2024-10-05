using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SoundHaven.Models;
using System.Text.RegularExpressions;
using TagLib;

namespace SoundHaven.Helpers
{
    public static class Mp3ToSongHelper
    {
        // Extracts the album cover and saves it to the Song object
        public static void SaveAlbumCover(Song song)
        {
            if (song == null || string.IsNullOrWhiteSpace(song.FilePath))
                throw new ArgumentException("Invalid song or file path.");

            if (!System.IO.File.Exists(song.FilePath))
                throw new FileNotFoundException("MP3 file not found.", song.FilePath);

            using (var file = TagLib.File.Create(song.FilePath))
            {
                if (file.Tag.Pictures.Length > 0)
                {
                    var picture = file.Tag.Pictures[0];
                    using (var stream = new MemoryStream(picture.Data.Data))
                    {
                        // Create a Bitmap from the stream
                        var bitmap = new Bitmap(stream);

                        // Set the Artwork property of the Song object
                        song.Artwork = bitmap;
                    }
                }
                else
                {
                    // No embedded artwork, search for image files in the song's directory
                    var songDirectory = Path.GetDirectoryName(song.FilePath);
                    if (Directory.Exists(songDirectory))
                    {
                        // Search for image files with .jpg, .jpeg, or .png extensions
                        var imageFiles = Directory.GetFiles(songDirectory, "*.*")
                            .Where(filePath => filePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                                            || filePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                                            || filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                            .ToArray();

                        if (imageFiles.Length > 0)
                        {
                            // Use the first image found
                            var imageFilePath = imageFiles[0];

                            // Read the image file into a byte array
                            var imageData = System.IO.File.ReadAllBytes(imageFilePath);

                            // Convert the byte array to ByteVector
                            var byteVector = new TagLib.ByteVector(imageData);

                            // Create a Picture object
                            TagLib.Picture picture = new TagLib.Picture
                            {
                                Type = TagLib.PictureType.FrontCover,
                                Description = "Cover",
                                MimeType = GetMimeType(imageFilePath),
                                Data = byteVector
                            };

                            // Assign the picture to the file's tag
                            file.Tag.Pictures = new TagLib.IPicture[] { picture };

                            // Save the changes to the file
                            file.Save();

                            // Set song.Artwork to the bitmap
                            using (var stream = new MemoryStream(imageData))
                            {
                                var bitmap = new Bitmap(stream);
                                song.Artwork = bitmap;
                            }
                        }
                        else
                        {
                            // Set song.Artwork to null or default image
                            song.Artwork = null;
                        }
                    }
                    else
                    {
                        // Set song.Artwork to null or default image
                        song.Artwork = null;
                    }
                }
            }
        }

        private static string GetMimeType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            switch (extension)
            {
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".png":
                    return "image/png";
                default:
                    return "application/octet-stream";
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
