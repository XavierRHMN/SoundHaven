using Avalonia.Controls;
using System;
using Avalonia.Media.Imaging;

namespace SoundHeaven.Models
{
    public class Song
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public TimeSpan Duration { get; set; }
        public string FilePath { get; set; }
        public Image? Artwork { get; set; }
        public string? ArtworkFilePath { get; set; }
        public string? Genre { get; set; }
        public int Year { get; set; }
        public double Length
        {
            get
            {
                return Duration.TotalSeconds;
            }
        }
    }
}
