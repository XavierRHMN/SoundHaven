using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SoundHaven.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace SoundHaven.Services
{
    public interface IDataService
    {
        Task<IEnumerable<Song>> GetTopTracksAsync();
        public Task<IEnumerable<Song>> GetRecentlyPlayedTracksAsync(string username);
        Task<IEnumerable<Song>> GetRecommendedAlbumsAsync(string username);
    }
    
    public class LastFmDataService : IDataService
    {
        private readonly LastfmClient _lastfmClient;
        private readonly IMemoryCache _cache;
        private readonly MemoryCacheEntryOptions _cacheOptions;

        public LastFmDataService(string apiKey, string apiSecret, IMemoryCache cache)
        {
            _lastfmClient = new LastfmClient(apiKey, apiSecret);
            _cache = cache;

            _cacheOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMinutes(30));
        }

        public async Task<IEnumerable<Song>> GetRecommendedAlbumsAsync(string username)
        {
            string cacheKey = $"recommended_tracks_{username}";

            if (_cache.TryGetValue(cacheKey, out IEnumerable<Song> cachedTracks))
            {
                return cachedTracks;
            }

            try
            {
                var topAlbums = await _lastfmClient.User.GetTopAlbums(username, new LastStatsTimeSpan(), 1);
                var albums = topAlbums.Select(album => new Song
                {
                    Title = album.Name,
                    Artist = album.ArtistName,
                    ArtworkUrl = album.Images.LastOrDefault()?.ToString() ?? string.Empty,
                    
                }).ToList();
                
                _cache.Set(cacheKey, albums, _cacheOptions);

                return albums;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching recommended tracks for user {username}: {ex}");
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
                    ArtworkUrl = track.Images.LastOrDefault()?.ToString() ?? string.Empty,
                }).ToList();

                _cache.Set(cacheKey, songs, _cacheOptions);

                return songs;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching top tracks: {ex}");
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
                    ArtworkUrl = track.Images.LastOrDefault()?.ToString() ?? string.Empty,
                }).ToList();

                _cache.Set(cacheKey, songs, _cacheOptions);

                return songs;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching recently played tracks for {username}: {ex}");
                return Enumerable.Empty<Song>();
            }
        }
    }
}