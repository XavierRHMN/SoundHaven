using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SoundHaven.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SoundHaven.Services
{
    public interface IDataService
    {
        Task<IEnumerable<Song>> GetTopTracksAsync();
        public Task<IEnumerable<Song>> GetRecentlyPlayedTracksAsync(string username);
        Task<IEnumerable<Song>> GetRecommendedTracksAsync(string username);
    }
    
    public class LastFmDataService : IDataService
    {
        private readonly LastfmClient _lastfmClient;
        private readonly IMemoryCache _cache;
        private readonly ILogger<LastFmDataService> _logger;
        private readonly MemoryCacheEntryOptions _cacheOptions;

        public LastFmDataService(string apiKey, string apiSecret, IMemoryCache cache, ILoggerFactory loggerFactory)
        {
            _lastfmClient = new LastfmClient(apiKey, apiSecret);
            _cache = cache;
            _logger = loggerFactory.CreateLogger<LastFmDataService>();

            _cacheOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMinutes(30));
        }

        public async Task<IEnumerable<Song>> GetRecommendedTracksAsync(string username)
        {
            string cacheKey = $"recommended_tracks_{username}";

            if (_cache.TryGetValue(cacheKey, out IEnumerable<Song> cachedTracks))
            {
                return cachedTracks;
            }

            try
            {
                var recommendations = await _lastfmClient.User.GetTopAlbums(username, new LastStatsTimeSpan(), 1);
                var songs = recommendations.Select(track => new Song
                {
                    Title = track.Name,
                    Artist = track.ArtistName,
                    
                }).ToList();
                
                _cache.Set(cacheKey, songs, _cacheOptions);

                return songs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching recommended tracks for user {Username}", username);
                return Enumerable.Empty<Song>();
            }
        }

        public async Task<IEnumerable<Song>> GetTopTracksAsync()
        {
            const string cacheKey = "top_tracks";

            if (_cache.TryGetValue(cacheKey, out IEnumerable<Song> cachedTracks))
            {
                return cachedTracks;
            }

            try
            {
                var topTracks = await _lastfmClient.Chart.GetTopTracksAsync();
                var songs = topTracks.Select(track => new Song
                {
                    Title = track.Name,
                    Artist = track.ArtistName,
                    // Map other properties as needed
                }).ToList();

                _cache.Set(cacheKey, songs, _cacheOptions);

                return songs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching top tracks");
                return Enumerable.Empty<Song>();
            }
        }

        public async Task<IEnumerable<Song>> GetRecentlyPlayedTracksAsync(string username)
        {
            string cacheKey = $"recently_played_{username}";

            if (_cache.TryGetValue(cacheKey, out IEnumerable<Song> cachedTracks))
            {
                return cachedTracks;
            }

            try
            {
                var recentTracks = await _lastfmClient.User.GetRecentScrobbles(username);
                var songs = recentTracks.Select(track => new Song
                {
                    Title = track.Name,
                    Artist = track.ArtistName,
                    // Map other properties as needed
                }).ToList();

                _cache.Set(cacheKey, songs, _cacheOptions);

                return songs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching recently played tracks for user {Username}", username);
                return Enumerable.Empty<Song>();
            }
        }
    }
}