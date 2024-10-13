using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Search;
using SoundHaven.Models;

namespace SoundHaven.Services
{
    public interface IYouTubeSearchService
    {
        Task<IEnumerable<VideoSearchResult>> SearchVideosAsync(string query, int limit);
    }

    public class YouTubeSearchService : IYouTubeSearchService
    {
        private readonly YoutubeClient _youtubeClient;

        public YouTubeSearchService()
        {
            _youtubeClient = new YoutubeClient();
        }

        public async Task<IEnumerable<VideoSearchResult>> SearchVideosAsync(string query, int limit)
        {
            var searchResults = await _youtubeClient.Search.GetVideosAsync(query).CollectAsync(limit);
            var tasks = searchResults.Select(async video =>
            {
                var videoDetails = await _youtubeClient.Videos.GetAsync(video.Id);
                return new VideoSearchResult
                {
                    VideoId = video.Id,
                    Title = video.Title,
                    Author = video.Author.ChannelTitle,
                    Duration = video.Duration,
                    ThumbnailUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url,
                    ViewCount = videoDetails.Engagement.ViewCount,
                    Year = videoDetails.UploadDate.Year
                };
            });

            return await Task.WhenAll(tasks);
        }
    }


    public class VideoSearchResult
    {
        public string VideoId { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public TimeSpan? Duration { get; set; }
        public string ThumbnailUrl { get; set; }
        public long ViewCount { get; set; }
        public int Year { get; set; }
    }
}
