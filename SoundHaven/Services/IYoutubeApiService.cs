using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using System;
using System.Collections.Generic;
using System.IO;
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
        
        public YouTubeApiService(string apiKey)
        {
            _youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = apiKey,
                ApplicationName = "SoundHaven"
            });
        }

        public async Task<IEnumerable<YouTubeVideoInfo>> SearchVideos(string query)
        {
            var searchListRequest = _youtubeService.Search.List("snippet");
            searchListRequest.Q = query;
            searchListRequest.Type = "video";
            searchListRequest.VideoCategoryId = "10"; // Music category
            searchListRequest.MaxResults = 20;

            var searchListResponse = await searchListRequest.ExecuteAsync();

            return searchListResponse.Items.Select(item => new YouTubeVideoInfo
            {
                Title = item.Snippet.Title,
                ChannelTitle = item.Snippet.ChannelTitle,
                ThumbnailUrl = item.Snippet.Thumbnails.Default__.Url,
                VideoId = item.Id.VideoId
            });
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
