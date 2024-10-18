using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Objects;
using IF.Lastfm.Core.Scrobblers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SoundHaven.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SoundHaven.Services
{
    public interface ILastFmDataService
    {
        Task<IEnumerable<Song>> GetTopTracksAsync();
        public Task<IEnumerable<Song>> GetRecentlyPlayedTracksAsync();
        Task<IEnumerable<Song>> GetRecommendedAlbumsAsync();
        public Task ScrobbleTrackAsync(string title, string artist, string album);
        Task<bool> UserExistsAsync(string username, string password);
        public string Username { get; set; }
        public string Password { get; set; }
    }
    
    public class LastFmLastFmDataService : ILastFmDataService
    {
        private readonly LastfmClient _lastfmClient;
        private readonly IMemoryCache _cache;
        private readonly MemoryCacheEntryOptions _cacheOptions;
        public string Username { get; set; }
        public string Password { get; set; }

        public LastFmLastFmDataService(string apiKey, string apiSecret, IMemoryCache cache)
        {
            _lastfmClient = new LastfmClient(apiKey, apiSecret, new HttpClient());
            _cache = cache;

            _cacheOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMinutes(30));
        }
        
        public async Task ScrobbleTrackAsync(string title, string artist, string album)
        {
            await _lastfmClient.Auth.GetSessionTokenAsync(Username, Password);
            try
            {
                var scrobble = new Scrobble(artist, album, title, DateTimeOffset.Now);
                await _lastfmClient.Scrobbler.ScrobbleAsync(scrobble);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error scrobbling track {title} by {artist}: {ex}");
            }
        }
        
        public async Task<bool> UserExistsAsync(string username, string password)
        {
            try
            {
                var response = await _lastfmClient.Auth.GetSessionTokenAsync(username, password);
                return response.Success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking user existence: {ex}");
                return false;
            }
        }

        public async Task<IEnumerable<Song>> GetRecommendedAlbumsAsync()
        {
            string cacheKey = $"recommended_tracks_{Username}";

            if (_cache.TryGetValue(cacheKey, out IEnumerable<Song> cachedTracks))
            {
                return cachedTracks;
            }

            try
            {
                var topAlbums = await _lastfmClient.User.GetTopAlbums(Username, new LastStatsTimeSpan(), 1);
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
                Debug.WriteLine($"Error fetching recommended tracks for user {Username}: {ex}");
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

        public async Task<IEnumerable<Song>> GetRecentlyPlayedTracksAsync()
        {
            string cacheKey = $"recently_played_{Username}";

            if (_cache.TryGetValue(cacheKey, out IEnumerable<Song> cachedTracks))
            {
                return cachedTracks;
            }

            try
            {
                var recentTracks = await _lastfmClient.User.GetRecentScrobbles(Username);
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
                Debug.WriteLine($"Error fetching recently played tracks for {Username}: {ex}");
                return Enumerable.Empty<Song>();
            }
        }
    }
}