using Avalonia.Controls;
using SoundHeaven.Services;
using System;

namespace SoundHeaven.Models
{
    public class Song
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public TimeSpan Duration { get; set; }
        public string? FilePath { get; set; }
        public Image? Artwork { get; set; }
        public string? ArtworkUrl { get; set; }
        public string? Genre { get; set; }
        public int Year { get; set; }
        public int PlayCount { get; set; }
        private double _length;
        public double Length
        {
            get => Duration.TotalSeconds;
            set
            {
                _length = value;
            }
        }

        public Song() { }
    }
}
