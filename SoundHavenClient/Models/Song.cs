using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SoundHaven.ViewModels;
using SoundHaven.Services;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Net.Http;

namespace SoundHaven.Models
{
    public class Song : ViewModelBase
    {
        
        // Existing properties
        public int Id { get; set; }
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public TimeSpan Duration { get; set; }
        public string? FilePath { get; set; }
        public string? Genre { get; set; }
        public int? Year { get; set; }
        public int PlayCount { get; set; }

        private double _length;
        public double Length
        {
            get
            {
                return Duration.TotalSeconds;
            }
            set
            {
                _length = value;
            }
        }

        // New YouTube-specific properties
        public bool IsYouTubeVideo
        {
            get
            {
                return !string.IsNullOrEmpty(VideoId);
            }
        }
        public string? VideoId { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? ChannelTitle { get; set; }
        public string? Views { get; set; }
        public string? VideoDuration { get; set; }

        private DownloadState _downloadState = DownloadState.NotDownloaded;
        public DownloadState CurrentDownloadState
        {
            get
            {
                return _downloadState;
            }
            set
            {
                SetProperty(ref _downloadState, value);
            }
        }

        private double _downloadProgress;
        public double DownloadProgress
        {
            get
            {
                return _downloadProgress;
            }
            set
            {
                SetProperty(ref _downloadProgress, value);
            }
        }
        private bool _isSelected;
        public bool IsSelected
        {
            get
            {
                return _isSelected;
            }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        private Bitmap? _artwork;
        public Bitmap? Artwork
        {
            get
            {
                return _artwork;
            }
            set
            {
                SetProperty(ref _artwork, value);
            }
        }

        private string? _artworkUrl;
        public string? ArtworkUrl
        {
            get
            {
                return _artworkUrl;
            }
            set
            {
                if (SetProperty(ref _artworkUrl, value))
                {
                    LoadArtwork();
                }
            }
        }

        // Existing methods
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

        // New method for loading YouTube thumbnail
        public async Task LoadYouTubeThumbnail()
        {
            if (!string.IsNullOrEmpty(ThumbnailUrl))
            {
                try
                {
                    using (var httpClient = new HttpClient())
                    {
                        byte[] imageBytes = await httpClient.GetByteArrayAsync(ThumbnailUrl);
                        using (var memoryStream = new MemoryStream(imageBytes))
                        {
                            Artwork = new Bitmap(memoryStream);
                        }
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
    }
}
