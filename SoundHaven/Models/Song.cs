using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SoundHaven.ViewModels;
using SoundHaven.Services;
using System;
using System.IO;

namespace SoundHaven.Models
{
    public class Song: ViewModelBase
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public TimeSpan Duration { get; set; }
        public string? FilePath { get; set; }
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
        
        private Bitmap? _artwork;
        public Bitmap? Artwork
        {
            get => _artwork;
            set
            {
                if (SetProperty(ref _artwork, value))
                {
                    UpdateAspectRatio();
                }
            }
        }

        private string? _artworkUrl;
        public string? ArtworkUrl
        {
            get => _artworkUrl;
            set
            {
                if (SetProperty(ref _artworkUrl, value))
                {
                    LoadArtwork();
                }
            }
        }

        private double _aspectRatio;
        public double AspectRatio
        {
            get => _aspectRatio;
            private set => SetProperty(ref _aspectRatio, value);
        }

        // New Methods
        private void LoadArtwork()
        {
            if (!string.IsNullOrEmpty(ArtworkUrl) && File.Exists(ArtworkUrl))
            {
                try
                {
                    using (var stream = File.OpenRead(ArtworkUrl))
                    {
                        Artwork = new Bitmap(stream);
                    }
                }
                catch
                {
                    Artwork = null;
                }
            }
            else
            {
                Artwork = null;
            }
        }

        private void UpdateAspectRatio()
        {
            if (Artwork != null && Artwork.PixelSize.Height != 0)
            {
                AspectRatio = Artwork.PixelSize.Width / (double)Artwork.PixelSize.Height;
            }
            else
            {
                AspectRatio = 1.0; // Default AspectRatio
            }
        }

        public Song() { }
    }
}
