using System;
using System.IO;
using Avalonia.Media.Imaging;
using SoundHeaven.Models;
using TagLib;

namespace SoundHeaven.Helpers
{
    public static class Mp3ToSongHelper
    {
        public static Song GetSongFromMp3(string filePath)
        {
            if (!System.IO.File.Exists(filePath))
                throw new FileNotFoundException("MP3 file not found.", filePath);

            var song = new Song();
            var file = TagLib.File.Create(filePath);

            // Extract basic metadata
            song.Title = file.Tag.Title;
            song.Artist = string.Join(", ", file.Tag.Performers);
            song.Album = file.Tag.Album;
            song.Genre = string.Join(", ", file.Tag.Genres);
            song.Year = (int)file.Tag.Year;
            song.Duration = file.Properties.Duration;
            song.FilePath = filePath;

            // Extract artwork if available
            if (file.Tag.Pictures.Length > 0)
            {
                var picture = file.Tag.Pictures[0];
                using (var stream = new MemoryStream(picture.Data.Data))
                {
                    song.Artwork = new Bitmap(stream);
                }
            }

            return song;
        }
    }
}
