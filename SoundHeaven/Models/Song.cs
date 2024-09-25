using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SoundHeaven.Services;
using SoundHeaven.ViewModels;
using System;

namespace SoundHeaven.Models
{
    public class Song: ViewModelBase
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public TimeSpan Duration { get; set; }
        public string? FilePath { get; set; }
        public Bitmap? Artwork { get; set; }
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
        
        private string? _artworkUrl;
        public string? ArtworkUrl
        {
            get => _artworkUrl;
            set
            {
                if (_artworkUrl != value)
                {
                    _artworkUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        public Song() { }
    }
}
