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

        public async Task<List<YouTubeVideoInfo>> SearchVideoByName(string query)
        {
            try
            {
                var response = await FetchYouTubeSearchResults(query);
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
            var encodedQuery = query.Contains("//:") ? query : HttpUtility.UrlEncode(query);
            return await client.GetStringAsync(YouTubeSearchUrl + encodedQuery);
        }

        private JObject ExtractInitialData(string response)
        {
            const string start = "var ytInitialData = ";
            const string end = "};";
            var startIndex = response.IndexOf(start) + start.Length;
            var endIndex = response.IndexOf(end, startIndex);
            var jsonData = response.Substring(startIndex, endIndex + 1 - startIndex);
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
            var title = videoRenderer?["title"]?["runs"]?[0]?["text"]?.ToString();
            var url = videoRenderer?["navigationEndpoint"]?["commandMetadata"]?["webCommandMetadata"]?["url"]?.ToString();
            var length = videoRenderer?["lengthText"]?["simpleText"]?.ToString();
            var views = videoRenderer?["shortViewCountText"]?["simpleText"]?.ToString();
            var channel = videoRenderer?["ownerText"]?["runs"]?[0]?["text"]?.ToString();
            var thumbnailUrl = videoRenderer?["thumbnail"]?["thumbnails"]?[0]?["url"]?.ToString();

            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(length) || string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(views))
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
                ThumbnailUrl = thumbnailUrl
            };
        }

        private string GetSafeFileName(string fileName)
        {
            return string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        }
    }


    public class YouTubeVideoInfo
    {
        public string Title { get; set; }
        public string ChannelTitle { get; set; }
        public string ThumbnailUrl { get; set; }
        public string VideoId { get; set; }
        public string ViewCount { get; set; }
        public string Duration { get; set; }
    }
}
