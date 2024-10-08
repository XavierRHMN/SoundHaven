using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Web;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;

namespace SoundHaven.Services
{
    public interface IYoutubeSearchService
    {
        Task<IEnumerable<YouTubeVideoInfo>> SearchVideos(string query);
    }
    
    public class YoutubeSearchService : IYoutubeSearchService
    {
        private readonly ILogger<YoutubeSearchService> _logger;
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromHours(24);
        private const string YouTubeSearchUrl = "https://www.youtube.com/results?search_query=";
        private const string YouTubeBase = "https://www.youtube.com";

        public YoutubeSearchService(ILoggerFactory loggerFactory, IMemoryCache memoryCache)
        {
            _logger = loggerFactory.CreateLogger<YoutubeSearchService>();
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

            _logger.LogInformation($"Searching YouTube for: {query}");

            var videos = await SearchVideoByName(query);

            if (videos != null && videos.Any())
            {
                // Cache the results
                _cache.Set(query, videos, _cacheDuration);
            }

            return videos ?? Enumerable.Empty<YouTubeVideoInfo>();
        }

        private async Task<List<YouTubeVideoInfo>> SearchVideoByName(string query)
        {
            try
            {
                string? response = await FetchYouTubeSearchResults(query);
                var initialData = ExtractInitialData(response);
                return ParseSearchResults(initialData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while searching YouTube videos");
                return null;
            }
        }

        private async Task<string> FetchYouTubeSearchResults(string query)
        {
            using var client = new HttpClient();
            string? encodedQuery = query.Contains("//:") ? query : HttpUtility.UrlEncode(query);
            return await client.GetStringAsync(YouTubeSearchUrl + encodedQuery);
        }

        private JObject ExtractInitialData(string response)
        {
            const string start = "var ytInitialData = ";
            const string end = "};";
            int startIndex = response.IndexOf(start) + start.Length;
            int endIndex = response.IndexOf(end, startIndex);
            string? jsonData = response.Substring(startIndex, endIndex + 1 - startIndex);
            return JObject.Parse(jsonData);
        }

        private List<YouTubeVideoInfo> ParseSearchResults(JObject initialData)
        {
            var videos = new List<YouTubeVideoInfo>();
            var results = initialData?["contents"]?["twoColumnSearchResultsRenderer"]?["primaryContents"]?["sectionListRenderer"]?["contents"]?[0]?["itemSectionRenderer"]?["contents"];

            if (results == null) return videos;

            foreach (var item in results)
            {
                var videoInfo = ParseVideoInfo(item["videoRenderer"]);
                if (videoInfo != null)
                {
                    videos.Add(videoInfo);
                }
            }

            return videos;
        }

        private YouTubeVideoInfo ParseVideoInfo(JToken videoRenderer)
        {
            string? title = videoRenderer?["title"]?["runs"]?[0]?["text"]?.ToString();
            string? url = videoRenderer?["navigationEndpoint"]?["commandMetadata"]?["webCommandMetadata"]?["url"]?.ToString();
            string? length = videoRenderer?["lengthText"]?["simpleText"]?.ToString();
            string? views = videoRenderer?["shortViewCountText"]?["simpleText"]?.ToString();
            string? channel = videoRenderer?["ownerText"]?["runs"]?[0]?["text"]?.ToString();
            string? thumbnailUrl = videoRenderer?["thumbnail"]?["thumbnails"]?[0]?["url"]?.ToString();
            string? publishedTimeText = videoRenderer?["publishedTimeText"]?["simpleText"]?.ToString();
            int? year = ExtractYear(publishedTimeText);

            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(length) || string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(views) || year == null)
            {
                return null;
            }

            return new YouTubeVideoInfo
            {
                Title = GetSafeFileName(title),
                VideoId = url.Replace("/watch?v=", ""),
                Duration = length,
                ChannelTitle = channel,
                ViewCount = views,
                ThumbnailUrl = thumbnailUrl,
                Year = year
            };
        }

        private int? ExtractYear(string? publishedTimeText)
        {
            int? year = null;
            if (!string.IsNullOrEmpty(publishedTimeText))
            {
                // Parse the publishedTimeText to estimate the year
                string[]? timeParts = publishedTimeText.Split(' ');
                if (timeParts.Length >= 2)
                {
                    if (int.TryParse(timeParts[0], out int number))
                    {
                        string? unit = timeParts[1].ToLower();
                        var currentDate = DateTime.Now;

                        if (unit.StartsWith("year"))
                        {
                            year = currentDate.Year - number;
                        }
                    }
                }
            }
            return year;
        }

        private string GetSafeFileName(string fileName) => string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
    }
    
    public class YouTubeVideoInfo
    {
        public string? Title { get; set; }
        public string? ChannelTitle { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? VideoId { get; set; }
        public string? ViewCount { get; set; }
        public string? Duration { get; set; }
        public int? Year { get; set; }
    }
}
