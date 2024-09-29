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
            List<YouTubeVideoInfo> videos = new List<YouTubeVideoInfo>();
            try
            {
                using (var client = new HttpClient())
                {
                    var queryEncoded = query.Contains("//:") ? query : HttpUtility.UrlEncode(query);
                    string response = await client.GetStringAsync(YouTubeSearchUrl + queryEncoded);
                    if (response != null)
                    {
                        string start = "var ytInitialData = ";
                        string end = "};";
                        var startIndex = response.IndexOf(start) + start.Length;
                        var endIndex = response.IndexOf(end, startIndex);

                        var initialData = JObject.Parse(response.Substring(startIndex, endIndex + 1 - startIndex));

                        var results = initialData?["contents"]?["twoColumnSearchResultsRenderer"]?["primaryContents"]?["sectionListRenderer"]?["contents"]?[0]?["itemSectionRenderer"]?["contents"];

                        if (results != null)
                        {
                            foreach (var item in results)
                            {
                                var video_info = item["videoRenderer"];
                                var title = video_info?["title"]?["runs"]?[0]?["text"];
                                var url = video_info?["navigationEndpoint"]?["commandMetadata"]?["webCommandMetadata"]?["url"];
                                var length = video_info?["lengthText"]?["simpleText"];
                                var views = video_info?["shortViewCountText"]?["simpleText"];
                                var channel = video_info?["ownerText"]?["runs"]?[0]?["text"];

                                if (title != null && url != null && length != null
                                    && channel != null && views != null)
                                {
                                    videos.Add(new YouTubeVideoInfo
                                    {
                                        Title = GetSafeFileName(title.ToString()),
                                        VideoId = url.ToString().Replace("/watch?v=", ""),
                                        Duration = length.ToString(),
                                        ChannelTitle = channel.ToString(),
                                        ViewCount = views.ToString(),
                                        ThumbnailUrl = video_info?["thumbnail"]?["thumbnails"]?[0]?["url"]?.ToString()
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while searching YouTube videos");
                return null;
            }

            return videos;
        }

        private string GetSafeFileName(string fileName)
        {
            return string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        }

        private ulong ParseViewCount(string viewCountString)
        {
            // Remove non-digit characters and parse
            string digitsOnly = new string(viewCountString.Where(c => char.IsDigit(c)).ToArray());
            return ulong.TryParse(digitsOnly, out ulong result) ? result : 0;
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