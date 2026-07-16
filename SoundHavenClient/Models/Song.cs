using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SoundHaven.Helpers;
using SoundHaven.ViewModels;

namespace SoundHaven.Models
{
    public class Song : ViewModelBase
    {
        private static readonly HttpClient ThumbnailHttpClient = CreateThumbnailHttpClient();

        // Existing properties with backing fields

        private int _id;
        public int Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private string? _title;
        public string? Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        private string? _artist;
        public string? Artist
        {
            get => _artist;
            set => SetProperty(ref _artist, value);
        }

        private string? _album;
        public string? Album
        {
            get => _album;
            set => SetProperty(ref _album, value);
        }

        private TimeSpan _duration;
        public TimeSpan Duration
        {
            get => _duration;
            set
            {
                if (SetProperty(ref _duration, value))
                {
                    // Notify that Length has changed when Duration changes
                    OnPropertyChanged(nameof(Length));
                }
            }
        }

        private string? _filePath;
        public string? FilePath
        {
            get => _filePath;
            set
            {
                if (SetProperty(ref _filePath, value))
                {
                    OnPropertyChanged(nameof(IsYouTubeVideo));
                }
            }
        }

        private string? _genre;
        public string? Genre
        {
            get => _genre;
            set => SetProperty(ref _genre, value);
        }

        private int? _year;
        public int? Year
        {
            get => _year;
            set => SetProperty(ref _year, value);
        }

        private int _playCount;
        public int PlayCount
        {
            get => _playCount;
            set => SetProperty(ref _playCount, value);
        }

        public double Length
        {
            get => Duration.TotalSeconds;
            set
            {
                if (value != Duration.TotalSeconds)
                {
                    Duration = TimeSpan.FromSeconds(value);
                }
            }
        }

        // YouTube-specific properties with backing fields

        private string? _videoId;
        public string? VideoId
        {
            get => _videoId;
            set
            {
                if (SetProperty(ref _videoId, value))
                {
                    // Notify that IsYouTubeVideo has changed when VideoId changes
                    OnPropertyChanged(nameof(IsYouTubeVideo));
                }
            }
        }

        public bool IsYouTubeVideo
        {
            get => !string.IsNullOrWhiteSpace(VideoId)
                && string.IsNullOrWhiteSpace(FilePath);
        }

        private string? _thumbnailUrl;
        public string? ThumbnailUrl
        {
            get => _thumbnailUrl;
            set
            {
                if (SetProperty(ref _thumbnailUrl, value)
                    && string.IsNullOrWhiteSpace(value)
                    && (_artworkData is null || _artworkData.Length == 0))
                {
                    Artwork = null;
                }
            }
        }

        private string? _channelTitle;
        public string? ChannelTitle
        {
            get => _channelTitle;
            set => SetProperty(ref _channelTitle, value);
        }

        private string? _views;
        public string? Views
        {
            get => _views;
            set => SetProperty(ref _views, value);
        }

        private string? _videoDuration;
        public string? VideoDuration
        {
            get => _videoDuration;
            set => SetProperty(ref _videoDuration, value);
        }

        // Download-related properties with backing fields

        private DownloadState _downloadState = DownloadState.NotDownloaded;
        public DownloadState CurrentDownloadState
        {
            get => _downloadState;
            set => SetProperty(ref _downloadState, value);
        }

        private double _downloadProgress;
        public double DownloadProgress
        {
            get => _downloadProgress;
            set => SetProperty(ref _downloadProgress, value);
        }

        // Selection property

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        // Artwork-related properties with backing fields

        private byte[] _artworkData = Array.Empty<byte>();
        public byte[] ArtworkData
        {
            get => _artworkData;
            set
            {
                if (SetProperty(ref _artworkData, value))
                {
                    // Clear the bitmap when the data changes
                    _artwork = null;
                    OnPropertyChanged(nameof(Artwork));
                }
            }
        }

        private Bitmap? _artwork;
        public Bitmap? Artwork
        {
            get
            {
                if (_artwork == null && _artworkData != null && _artworkData.Length > 0)
                {
                    // Lazy load the bitmap
                    using (var memoryStream = new MemoryStream(_artworkData))
                    {
                        _artwork = new Bitmap(memoryStream);
                    }
                }
                return _artwork;
            }
            set => SetProperty(ref _artwork, value);
        }

        private string? _artworkUrl;
        public string? ArtworkUrl
        {
            get => _artworkUrl;
            set => SetProperty(ref _artworkUrl, value);
        }

        // Methods

        public void SetArtworkData(Bitmap bitmap)
        {
            using (var memoryStream = new MemoryStream())
            {
                bitmap.Save(memoryStream);
                ArtworkData = memoryStream.ToArray();
            }
        }

        public Song CloneForQueue()
        {
            return new Song
            {
                Title = Title,
                Artist = Artist,
                Album = Album,
                Duration = Duration,
                FilePath = FilePath,
                Genre = Genre,
                Year = Year,
                VideoId = VideoId,
                ThumbnailUrl = ThumbnailUrl,
                ChannelTitle = ChannelTitle,
                Views = Views,
                VideoDuration = VideoDuration,
                ArtworkData = ArtworkData is { Length: > 0 }
                    ? (byte[])ArtworkData.Clone()
                    : Array.Empty<byte>(),
                ArtworkUrl = ArtworkUrl,
                CurrentDownloadState = CurrentDownloadState
            };
        }

        public async Task LoadThumbnailAsync(
            HttpClient? httpClient = null,
            bool forceReload = false,
            CancellationToken cancellationToken = default)
        {
            if (!forceReload
                && _artworkData is { Length: > 0 }
                && _artwork is not null)
            {
                return;
            }

            string? sourceUrl = YouTubeThumbnailHelper.UpgradeThumbnailUrl(ThumbnailUrl);
            if (!string.IsNullOrWhiteSpace(sourceUrl)
                && !string.Equals(ThumbnailUrl, sourceUrl, StringComparison.Ordinal))
            {
                ThumbnailUrl = sourceUrl;
            }

            if (string.IsNullOrWhiteSpace(ThumbnailUrl) && string.IsNullOrWhiteSpace(VideoId))
            {
                return;
            }

            HttpClient client = httpClient ?? ThumbnailHttpClient;
            foreach (string candidateUrl in YouTubeThumbnailHelper.GetDownloadCandidates(
                         ThumbnailUrl,
                         VideoId))
            {
                try
                {
                    using HttpResponseMessage response = await client.GetAsync(
                        candidateUrl,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    byte[] imageBytes = await response.Content
                        .ReadAsByteArrayAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (imageBytes.Length == 0
                        || YouTubeThumbnailHelper.LooksLikeMissingYouTubeThumbnail(imageBytes))
                    {
                        continue;
                    }

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ArtworkData = imageBytes;
                        ThumbnailUrl = candidateUrl;
                    });
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (HttpRequestException)
                {
                    // Try the next higher/fallback thumbnail candidate.
                }
                catch (IOException)
                {
                    // Try the next higher/fallback thumbnail candidate.
                }
            }
        }

        public bool NeedsHigherQualityArtwork()
        {
            if (string.IsNullOrWhiteSpace(ThumbnailUrl) && string.IsNullOrWhiteSpace(VideoId))
            {
                return false;
            }

            if (Artwork is null || ArtworkData is null || ArtworkData.Length == 0)
            {
                return true;
            }

            return Artwork.PixelSize.Width < YouTubeThumbnailHelper.LowQualityPixelThreshold
                || Artwork.PixelSize.Height < YouTubeThumbnailHelper.LowQualityPixelThreshold;
        }

        private static HttpClient CreateThumbnailHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
                + "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("image/*");
            client.DefaultRequestHeaders.Referrer = new Uri("https://music.youtube.com/");
            return client;
        }
    }
}
