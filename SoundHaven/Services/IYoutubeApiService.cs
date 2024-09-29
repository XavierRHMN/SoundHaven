using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SoundHaven.Services
{
    public interface IYouTubeApiService
    {
        Task<IEnumerable<YouTubeVideoInfo>> SearchVideos(string query);
    }

    public class YouTubeApiService : IYouTubeApiService
    {
        private readonly YouTubeService _youtubeService;
        private readonly ILogger<YouTubeApiService> _logger;
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromHours(24);
        private int _dailyRequestCount = 0;
        private DateTime _lastResetTime = DateTime.UtcNow;

        public YouTubeApiService(string apiKey, ILoggerFactory logger, IMemoryCache memoryCache)
        {
            _youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = apiKey,
                ApplicationName = "SoundHaven"
            });
            _logger = logger.CreateLogger<YouTubeApiService>();;
            _cache = memoryCache;
        }

        public async Task<IEnumerable<YouTubeVideoInfo>> SearchVideos(string query)
        {
            // Check if the query result is in the cache
            if (_cache.TryGetValue(query, out IEnumerable<YouTubeVideoInfo> cachedResult))
            {
                _logger.LogInformation($"Returning cached result for query: {query}");
                return cachedResult;
            }

            // Reset daily count if it's a new day
            if (DateTime.UtcNow.Date > _lastResetTime.Date)
            {
                _dailyRequestCount = 0;
                _lastResetTime = DateTime.UtcNow;
                _logger.LogInformation("Daily request count reset");
            }

            // Check if we've exceeded the daily limit
            if (_dailyRequestCount >= 100) // Adjust this number based on your needs
            {
                _logger.LogWarning("Daily YouTube API query limit reached. Request blocked.");
                return Enumerable.Empty<YouTubeVideoInfo>();
            }

            try
            {
                _logger.LogInformation($"Querying YouTube API for: {query}");

                var searchListRequest = _youtubeService.Search.List("snippet");
                searchListRequest.Q = query;
                searchListRequest.Type = "video";
                searchListRequest.VideoCategoryId = "10"; // Music category
                searchListRequest.MaxResults = 20;

                var searchListResponse = await searchListRequest.ExecuteAsync();

                _dailyRequestCount++;
                _logger.LogInformation($"YouTube API request completed. Total requests today: {_dailyRequestCount}");

                var results = searchListResponse.Items.Select(item => new YouTubeVideoInfo
                {
                    Title = item.Snippet.Title,
                    ChannelTitle = item.Snippet.ChannelTitle,
                    ThumbnailUrl = item.Snippet.Thumbnails.Default__.Url,
                    VideoId = item.Id.VideoId
                }).ToList();

                // Cache the results
                _cache.Set(query, results, _cacheDuration);

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while searching YouTube videos");
                return Enumerable.Empty<YouTubeVideoInfo>();
            }
        }
    }

    public class YouTubeVideoInfo
    {
        public string? Title { get; set; }
        public string? ChannelTitle { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? VideoId { get; set; }
    }
}