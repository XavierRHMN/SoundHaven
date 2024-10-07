using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Media;
using Avalonia;
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
                Bitmap bitmap = null;

                if (file.Tag.Pictures.Length > 0)
                {
                    var picture = file.Tag.Pictures[0];
                    using (var stream = new MemoryStream(picture.Data.Data))
                    {
                        // Create a Bitmap from the stream
                        bitmap = new Bitmap(stream);
                    }
                }
                else
                {
                    // No embedded artwork, search for image files in the song's directory
                    string? songDirectory = Path.GetDirectoryName(song.FilePath);
                    if (Directory.Exists(songDirectory))
                    {
                        // Search for image files with .jpg, .jpeg, or .png extensions
                        string[]? imageFiles = Directory.GetFiles(songDirectory, "*.*")
                            .Where(filePath => filePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                                || filePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                                || filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                            .ToArray();

                        if (imageFiles.Length > 0)
                        {
                            // Use the first image found
                            string? imageFilePath = imageFiles[0];

                            // Read the image file into a bitmap
                            using (var stream = new FileStream(imageFilePath, FileMode.Open, FileAccess.Read))
                            {
                                bitmap = new Bitmap(stream);
                            }

                            // Embed the image into the MP3 file
                            byte[]? imageData = System.IO.File.ReadAllBytes(imageFilePath);
                            var byteVector = new ByteVector(imageData);

                            // Create a Picture object
                            var picture = new Picture
                            {
                                Type = PictureType.FrontCover,
                                Description = "Cover",
                                MimeType = GetMimeType(imageFilePath),
                                Data = byteVector
                            };

                            // Assign the picture to the file's tag
                            file.Tag.Pictures = new IPicture[] { picture };

                            // Save the changes to the file
                            file.Save();
                        }
                    }
                }

                if (bitmap != null)
                {
                    // Process the bitmap to add black bars and make it 16:9
                    var processedBitmap = AddBlackBarsToMake16by9(bitmap);

                    // Set the Artwork property of the Song object
                    song.Artwork = processedBitmap;
                }
                else
                {
                    // Set song.Artwork to null or default image
                    song.Artwork = null;
                }
            }
        }

        private static Bitmap AddBlackBarsToMake16by9(Bitmap originalBitmap)
        {
            // Get original dimensions
            int originalWidth = originalBitmap.PixelSize.Width;
            int originalHeight = originalBitmap.PixelSize.Height;

            // Calculate the new width to make the aspect ratio 16:9
            int newWidth = (int)Math.Round(originalHeight * 16.0 / 9.0);

            // If the new width is less than the original width, adjust the height instead
            if (newWidth < originalWidth)
            {
                newWidth = originalWidth;
                // Adjust height to match 16:9
                originalHeight = (int)Math.Round(newWidth * 9.0 / 16.0);
            }

            // Create a new RenderTargetBitmap with the new dimensions
            var targetBitmap = new RenderTargetBitmap(new PixelSize(newWidth, originalHeight), new Vector(96, 96));

            using (var ctx = targetBitmap.CreateDrawingContext(false))
            {
                // Fill the background with black
                ctx.FillRectangle(Brushes.Black, new Rect(0, 0, newWidth, originalHeight));

                // Calculate the position to center the original image
                double x = (newWidth - originalBitmap.PixelSize.Width) / 2.0;
                double y = (originalHeight - originalBitmap.PixelSize.Height) / 2.0;

                // Draw the original image onto the new image
                ctx.DrawImage(originalBitmap,
                    new Rect(0, 0, originalBitmap.PixelSize.Width, originalBitmap.PixelSize.Height),
                    new Rect(x, y, originalBitmap.PixelSize.Width, originalBitmap.PixelSize.Height));
            }

            return targetBitmap;
        }

        private static string GetMimeType(string fileName)
        {
            string? extension = Path.GetExtension(fileName).ToLowerInvariant();
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
