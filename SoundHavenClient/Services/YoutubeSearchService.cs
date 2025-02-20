using SoundHaven.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Common;
using YouTubeMusicAPI.Client;
using Song = YouTubeMusicAPI.Models.Search.SongSearchResult;

namespace SoundHaven.Services
{
    public interface IYouTubeSearchService
    {
        Task<IEnumerable<VideoSearchResult>> SearchAsync(string query, int limit, bool searchSongs);
    }

    public class YouTubeSearchService : IYouTubeSearchService
    {
        private readonly YoutubeClient _youtubeClient;
        private readonly YouTubeMusicClient _youTubeMusicAPI;

        public YouTubeSearchService()
        {
            _youtubeClient = new YoutubeClient();
            _youTubeMusicAPI = new YouTubeMusicClient();
        }

        public async Task<IEnumerable<VideoSearchResult>> SearchAsync(string query, int limit, bool searchSongs)
        {
            if (searchSongs)
            {
                IEnumerable<Song> searchResults = await _youTubeMusicAPI.SearchAsync<Song>(query);
                var tasks = searchResults.Take(limit).Select(async video =>
                {
                    var videoDetails = await _youtubeClient.Videos.GetAsync(video.Id);
                    return new VideoSearchResult
                    {
                        VideoId = video.Id,
                        Title = video.Name,
                        Author = video.Artists.FirstOrDefault()?.Name,
                        Album = video.Album.Name,
                        Duration = video.Duration,
                        ThumbnailUrl = GetHighQualityThumbnailUrl(video.Id),
                        ViewCount = videoDetails.Engagement.ViewCount,
                        Year = videoDetails.UploadDate.Year
                    };
                });

                return await Task.WhenAll(tasks);
            }
            else
            {
                IReadOnlyList<YoutubeExplode.Search.VideoSearchResult> searchResults = await _youtubeClient.Search.GetVideosAsync(query).CollectAsync(limit);
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
        
        private string GetHighQualityThumbnailUrl(string videoId)
        {
            // Try maxres thumbnail first
            string maxresUrl = $"https://img.youtube.com/vi/{videoId}/maxresdefault.jpg";
            if (ThumbnailExists(maxresUrl))
                return maxresUrl;

            // Fall back to sddefault if maxres is not available
            string sddefaultUrl = $"https://img.youtube.com/vi/{videoId}/sddefault.jpg";
            if (ThumbnailExists(sddefaultUrl))
                return sddefaultUrl;

            // If all else fails, use hqdefault
            return $"https://img.youtube.com/vi/{videoId}/hqdefault.jpg";
        }

        private bool ThumbnailExists(string url)
        {
            try
            {
                var request = WebRequest.Create(url);
                request.Method = "HEAD";
                using (var response = request.GetResponse() as HttpWebResponse)
                {
                    return response.StatusCode == HttpStatusCode.OK;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    public class VideoSearchResult
    {
        public string? VideoId { get; set; }
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? Album { get; set; }
        public TimeSpan? Duration { get; set; }
        public string? ThumbnailUrl { get; set; }
        public long ViewCount { get; set; }
        public int Year { get; set; }
    }
}
