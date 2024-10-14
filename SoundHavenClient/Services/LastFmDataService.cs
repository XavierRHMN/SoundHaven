// Services/LastFmDataService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SoundHaven.Models;
using System.IO;
using System.Diagnostics; // Added for Debug.WriteLine

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
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly IMemoryCache _cache;
        private readonly ILogger<LastFmDataService> _logger;
        private readonly MemoryCacheEntryOptions _cacheOptions;

        public LastFmDataService(string apiKey, IMemoryCache cache, ILoggerFactory loggerFactory)
        {
            _apiKey = apiKey;
            _cache = cache;
            _logger = loggerFactory.CreateLogger<LastFmDataService>();
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://ws.audioscrobbler.com/2.0/")
            };

            // Define cache options with sliding expiration of 30 minutes
            _cacheOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMinutes(30));
        }

        #region Top Tracks

        /// <summary>
        /// Fetches the top tracks globally.
        /// </summary>
        public async Task<IEnumerable<Song>> GetTopTracksAsync()
        {
            string cacheKey = "global-top-tracks";

            if (_cache.TryGetValue(cacheKey, out IEnumerable<Song> cachedTopTracks))
            {
                Debug.WriteLine("Retrieved Top Tracks from cache.");
                return cachedTopTracks;
            }

            int limit = 20; // Limit to 20 top tracks
            string? url = $"?method=chart.gettoptracks&api_key={_apiKey}&format=json&limit={limit}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                string? content = await response.Content.ReadAsStringAsync();
                var topTracksResponse = JsonConvert.DeserializeObject<TopTracksResponse>(content);

                var songs = new List<Song>();

                foreach (var track in topTracksResponse?.Tracks?.TrackList?.Take(limit) ?? Enumerable.Empty<LastFmTrack>())
                {
                    var song = new Song
                    {
                        Title = track.Name,
                        Artist = track.Artist.Name,
                        Length = int.TryParse(track.Duration, out int duration) ? duration : 0
                    };

                    // Fetch the album image, or artist image if album image is not available
                    song.ArtworkUrl = await GetAlbumImageUrlAsync(track.Artist.Name, track.Name);

                    // Log the artwork URL for debugging
                    Debug.WriteLine($"Artwork URL for {song.Title} by {song.Artist}: {song.ArtworkUrl}");

                    songs.Add(song);
                }

                // Add to cache
                _cache.Set(cacheKey, songs, _cacheOptions);
                Debug.WriteLine("Cached Top Tracks.");

                return songs;
            }
            else
            {
                string? errorContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Error fetching top tracks: {response.StatusCode}, Content: {errorContent}");
                return Enumerable.Empty<Song>();
            }
        }

        #endregion

        #region User's Top Artists

        /// <summary>
        /// Fetches the user's top artists.
        /// </summary>
        public async Task<IEnumerable<string>> GetUserTopArtistsAsync(string username, int limit = 5)
        {
            if (string.IsNullOrEmpty(username))
            {
                throw new ArgumentException("Username cannot be null or empty.", nameof(username));
            }

            string cacheKey = $"user-top-artists-{username}";

            if (_cache.TryGetValue(cacheKey, out IEnumerable<string> cachedTopArtists))
            {
                Debug.WriteLine($"Retrieved Top Artists for '{username}' from cache.");
                return cachedTopArtists;
            }

            string? url = $"?method=user.gettopartists&user={Uri.EscapeDataString(username)}&api_key={_apiKey}&format=json&limit={limit}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                string? content = await response.Content.ReadAsStringAsync();
                var userTopArtistsResponse = JsonConvert.DeserializeObject<UserTopArtistsResponse>(content);

                var topArtists = userTopArtistsResponse?.TopArtists?.ArtistList
                    .Select(a => a.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();

                if (topArtists != null && topArtists.Any())
                {
                    _cache.Set(cacheKey, topArtists, _cacheOptions);
                    Debug.WriteLine($"Fetched and cached Top Artists for '{username}'.");
                    return topArtists;
                }
                else
                {
                    Debug.WriteLine($"No top artists found for user '{username}'.");
                }
            }
            else
            {
                string? errorContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Error fetching top artists for user {username}: {response.StatusCode}, Content: {errorContent}");
            }

            return Enumerable.Empty<string>();
        }

        #endregion

        #region Artist's Top Tracks

        /// <summary>
        /// Fetches the top tracks for a given artist.
        /// </summary>
        public async Task<IEnumerable<Song>> GetTopTracksByArtistAsync(string artistName, int limit = 5)
        {
            if (string.IsNullOrEmpty(artistName))
            {
                Debug.WriteLine("Artist name is null or empty.");
                return Enumerable.Empty<Song>();
            }

            string cacheKey = $"artist-top-tracks-{artistName}";

            if (_cache.TryGetValue(cacheKey, out IEnumerable<Song> cachedTopTracks))
            {
                Debug.WriteLine($"Retrieved Top Tracks for artist '{artistName}' from cache.");
                return cachedTopTracks;
            }

            string? url = $"?method=artist.gettoptracks&artist={Uri.EscapeDataString(artistName)}&api_key={_apiKey}&format=json&limit={limit}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                string? content = await response.Content.ReadAsStringAsync();
                var topTracksResponse = JsonConvert.DeserializeObject<ArtistTopTracksResponse>(content);

                var songs = topTracksResponse?.TopTracks?.TrackList
                    .Select(track => new Song
                    {
                        Title = track.Name,
                        Artist = artistName,
                        Length = int.TryParse(track.Duration, out int duration) ? duration : 0,
                        ArtworkUrl = null // Optionally fetch artwork here or use existing methods
                    })
                    .Where(song => song != null)
                    .ToList();

                if (songs != null && songs.Any())
                {
                    _cache.Set(cacheKey, songs, _cacheOptions);
                    Debug.WriteLine($"Fetched and cached Top Tracks for artist '{artistName}'.");
                    return songs;
                }
                else
                {
                    Debug.WriteLine($"No top tracks found for artist '{artistName}'.");
                }
            }
            else
            {
                string? errorContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Error fetching top tracks for {artistName}: {response.StatusCode}, Content: {errorContent}");
            }

            return Enumerable.Empty<Song>();
        }

        #endregion

        #region Recommended Tracks

        /// <summary>
        /// Generates recommended tracks based on the user's top artists and their top tracks.
        /// </summary>
        public async Task<IEnumerable<Song>> GetRecommendedTracksAsync(string username)
        {
            var recommendedTracks = new List<Song>();
            int maxRecommended = 20;
            int topArtistsLimit = 5; // Number of top artists to fetch
            int topTracksPerArtist = 4; // Number of top tracks to fetch per artist

            // Step 1: Fetch user's top artists
            var topArtists = await GetUserTopArtistsAsync(username, topArtistsLimit);

            foreach (string? artist in topArtists)
            {
                // Step 2: Fetch top tracks for each top artist
                var artistTopTracks = await GetTopTracksByArtistAsync(artist, topTracksPerArtist);
                recommendedTracks.AddRange(artistTopTracks);

                if (recommendedTracks.Count >= maxRecommended)
                    break;
            }

            // Step 3: Remove duplicates and limit to maxRecommended
            recommendedTracks = recommendedTracks
                .GroupBy(t => new { t.Title, t.Artist })
                .Select(g => g.First())
                .Take(maxRecommended)
                .ToList();

            // Step 4: Optionally, fetch artwork URLs for the recommended tracks
            foreach (var song in recommendedTracks)
            {
                if (string.IsNullOrEmpty(song.ArtworkUrl))
                {
                    song.ArtworkUrl = await GetAlbumImageUrlAsync(song.Artist, song.Title);
                }
            }

            Debug.WriteLine($"Generated {recommendedTracks.Count} recommended tracks for user '{username}'.");
            return recommendedTracks;
        }

        #endregion

        #region Recently Played Tracks

        /// <summary>
        /// Fetches the recently played tracks for a given user.
        /// </summary>
        public async Task<IEnumerable<Song>> GetRecentlyPlayedTracksAsync(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                throw new ArgumentException("Username cannot be null or empty.", nameof(username));
            }

            string cacheKey = $"recently-played-{username}";

            if (_cache.TryGetValue(cacheKey, out IEnumerable<Song> cachedRecentlyPlayed))
            {
                Debug.WriteLine($"Retrieved Recently Played Tracks for '{username}' from cache.");
                return cachedRecentlyPlayed;
            }

            int limit = 20; // Limit to 20 recent tracks
            string? url = $"?method=user.getrecenttracks&user={Uri.EscapeDataString(username)}&api_key={_apiKey}&format=json&limit={limit}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                string? content = await response.Content.ReadAsStringAsync();
                var recentTracksResponse = JsonConvert.DeserializeObject<RecentTracksResponse>(content);

                var songs = new List<Song>();

                foreach (var track in recentTracksResponse?.RecentTracks?.TrackList?.Take(limit) ?? Enumerable.Empty<RecentTrack>())
                {
                    var song = new Song
                    {
                        Title = track.Name,
                        Artist = track.Artist.Name,
                        Length = 0 // Duration may not be available
                    };

                    // Fetch the album image, or artist image if album image is not available
                    song.ArtworkUrl = await GetAlbumImageUrlAsync(track.Artist.Name, track.Name);

                    songs.Add(song);
                }

                // Add to cache
                _cache.Set(cacheKey, songs, _cacheOptions);
                Debug.WriteLine($"Fetched and cached Recently Played Tracks for '{username}'.");

                return songs;
            }
            else
            {
                string? errorContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Error fetching recent tracks for user {username}: {response.StatusCode}, Content: {errorContent}");
                return Enumerable.Empty<Song>();
            }
        }

        #endregion

        #region Similar Artists (Optional)

        /// <summary>
        /// Fetches similar artists for a given artist.
        /// </summary>
        private async Task<IEnumerable<string>> GetSimilarArtistsAsync(string artistName, int limit = 5)
        {
            if (string.IsNullOrEmpty(artistName))
            {
                Debug.WriteLine("Artist name is null or empty.");
                return Enumerable.Empty<string>();
            }

            string cacheKey = $"similar-artists-{artistName}";

            if (_cache.TryGetValue(cacheKey, out IEnumerable<string> cachedSimilarArtists))
            {
                Debug.WriteLine($"Retrieved Similar Artists for '{artistName}' from cache.");
                return cachedSimilarArtists;
            }

            string? url = $"?method=artist.getsimilar&artist={Uri.EscapeDataString(artistName)}&api_key={_apiKey}&format=json&limit={limit}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                string? content = await response.Content.ReadAsStringAsync();
                var similarArtistsResponse = JsonConvert.DeserializeObject<SimilarArtistsResponse>(content);

                var similarArtists = similarArtistsResponse?.SimilarArtists?.ArtistList
                    .Select(a => a.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();

                if (similarArtists != null && similarArtists.Any())
                {
                    _cache.Set(cacheKey, similarArtists, _cacheOptions);
                    Debug.WriteLine($"Fetched and cached Similar Artists for '{artistName}'.");
                    return similarArtists;
                }
                else
                {
                    Debug.WriteLine($"No similar artists found for '{artistName}'.");
                }
            }
            else
            {
                string? errorContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Error fetching similar artists for {artistName}: {response.StatusCode}, Content: {errorContent}");
            }

            return Enumerable.Empty<string>();
        }

        #endregion

        #region Album Image Retrieval

        /// <summary>
        /// Retrieves the album image URL for a given artist and track.
        /// </summary>
        public async Task<string> GetAlbumImageUrlAsync(string artistName, string trackName)
        {
            if (string.IsNullOrEmpty(artistName) || string.IsNullOrEmpty(trackName))
            {
                Debug.WriteLine("Artist name or track name is null or empty.");
                return "https://example.com/default-artwork.png"; // Replace with your default image URL
            }

            string cacheKey = $"album-image-{artistName}-{trackName}";

            if (_cache.TryGetValue(cacheKey, out string cachedImageUrl))
            {
                Debug.WriteLine($"Retrieved Album Image for '{artistName} - {trackName}' from cache.");
                return cachedImageUrl;
            }

            string? url = $"?method=track.getInfo&api_key={_apiKey}&artist={Uri.EscapeDataString(artistName)}&track={Uri.EscapeDataString(trackName)}&format=json";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                string? content = await response.Content.ReadAsStringAsync();

                var trackInfoResponse = JsonConvert.DeserializeObject<TrackInfoResponse>(content);

                var album = trackInfoResponse?.Track?.Album;

                string? imageUrl = album?.Images?.FirstOrDefault(img => img.Size == "large")?.Url
                    ?? album?.Images?.FirstOrDefault(img => img.Size == "medium")?.Url;

                if (string.IsNullOrEmpty(imageUrl) || IsPlaceholderImage(imageUrl))
                {
                    // Attempt to find an album image by searching for other tracks with the same name
                    imageUrl = await GetAlbumImageUrlByTrackSearchAsync(trackName);
                }

                if (string.IsNullOrEmpty(imageUrl))
                {
                    imageUrl = await GetArtistImageUrlAsync(artistName);
                }

                // Add to cache
                _cache.Set(cacheKey, imageUrl, _cacheOptions);
                Debug.WriteLine($"Cached Album Image for '{artistName} - {trackName}'.");

                return imageUrl;
            }
            else
            {
                string? errorContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Error fetching album info for {trackName} by {artistName}: {response.StatusCode}, Content: {errorContent}");
                return "https://example.com/default-artwork.png"; // Replace with your default image URL
            }
        }

        /// <summary>
        /// Retrieves the artist's image URL.
        /// </summary>
        public async Task<string> GetArtistImageUrlAsync(string artistName)
        {
            if (string.IsNullOrEmpty(artistName))
            {
                Debug.WriteLine("Artist name is null or empty.");
                return "https://example.com/default-artwork.png"; // Replace with your default image URL
            }

            string cacheKey = $"artist-image-{artistName}";

            if (_cache.TryGetValue(cacheKey, out string cachedImageUrl))
            {
                Debug.WriteLine($"Retrieved Artist Image for '{artistName}' from cache.");
                return cachedImageUrl;
            }

            string[] artistNameVariants = new string[]
            {
                artistName,
                artistName.Replace(",", ""), // Remove commas
                artistName.Replace(",", "").Replace("'", ""), // Remove commas and apostrophes
                artistName.ToLower(), // Lowercase
                artistName.ToUpper() // Uppercase
                // Add more variants if necessary
            };

            foreach (string? nameVariant in artistNameVariants)
            {
                string? encodedArtistName = Uri.EscapeDataString(nameVariant);
                string? url = $"?method=artist.getinfo&artist={encodedArtistName}&api_key={_apiKey}&format=json";

                Debug.WriteLine($"Requesting artist info from URL: {_httpClient.BaseAddress}{url}");

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    string? content = await response.Content.ReadAsStringAsync();

                    var artistInfoResponse = JsonConvert.DeserializeObject<ArtistInfoResponse>(content);

                    if (artistInfoResponse?.Error != 0 || artistInfoResponse?.Artist == null)
                    {
                        Debug.WriteLine($"API Error or no artist data for '{nameVariant}': {artistInfoResponse?.Message}");
                        continue;
                    }

                    string? imageUrl = artistInfoResponse.Artist.Images?
                            .FirstOrDefault(img => img.Size == "extralarge")?.Url
                        ?? artistInfoResponse.Artist.Images?.FirstOrDefault(img => img.Size == "large")?.Url
                        ?? artistInfoResponse.Artist.Images?.FirstOrDefault(img => img.Size == "medium")?.Url
                        ?? artistInfoResponse.Artist.Images?.FirstOrDefault(img => img.Size == "small")?.Url;

                    Debug.WriteLine($"Artist image URL for '{nameVariant}': {imageUrl}");

                    if (!string.IsNullOrEmpty(imageUrl) && !IsPlaceholderImage(imageUrl))
                    {
                        _cache.Set(cacheKey, imageUrl, _cacheOptions);
                        Debug.WriteLine($"Cached Artist Image for '{artistName}'.");
                        return imageUrl;
                    }
                    else
                    {
                        Debug.WriteLine($"No valid image found for '{nameVariant}'. Attempting next variant.");
                    }
                }
                else
                {
                    string? errorContent = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"Error fetching artist info for '{nameVariant}': {response.StatusCode}, Content: {errorContent}");
                }
            }

            // If no valid image URL found, set to default image
            _cache.Set(cacheKey, "https://example.com/default-artwork.png", _cacheOptions); // Replace with your default image URL
            Debug.WriteLine($"No valid artist image found for '{artistName}'. Set to default image.");
            return "https://example.com/default-artwork.png"; // Replace with your default image URL
        }

        /// <summary>
        /// Determines if the given image URL is a placeholder.
        /// </summary>
        private bool IsPlaceholderImage(string imageUrl)
        {
            var placeholderImageUrls = new HashSet<string>
            {
                "https://lastfm.freetls.fastly.net/i/u/300x300/2a96cbd8b46e442fc41c2b86b821562f.png",
                "https://lastfm.freetls.fastly.net/i/u/174s/empty-star.png",
                "https://lastfm.freetls.fastly.net/i/u/300x300/empty-star.png"
                // Add other known placeholder URLs
            };

            if (placeholderImageUrls.Contains(imageUrl))
                return true;

            // Additional heuristic checks
            if (imageUrl.Contains("empty-star") || imageUrl.Contains("no-image"))
                return true;

            return false;
        }

        #endregion

        #region Similar Tracks (Optional)

        /// <summary>
        /// Fetches similar tracks for a given artist and track.
        /// </summary>
        private async Task<IEnumerable<Song>> GetSimilarTracksAsync(string artistName, string trackName, int limit = 10)
        {
            if (string.IsNullOrEmpty(artistName) || string.IsNullOrEmpty(trackName))
            {
                Debug.WriteLine("Artist name or track name is null or empty.");
                return Enumerable.Empty<Song>();
            }

            string cacheKey = $"similar-tracks-{artistName}-{trackName}";

            if (_cache.TryGetValue(cacheKey, out IEnumerable<Song> cachedSimilarTracks))
            {
                Debug.WriteLine($"Retrieved Similar Tracks for '{artistName} - {trackName}' from cache.");
                return cachedSimilarTracks;
            }

            string? url = $"?method=track.getsimilar&artist={Uri.EscapeDataString(artistName)}&track={Uri.EscapeDataString(trackName)}&api_key={_apiKey}&format=json&limit={limit}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                string? content = await response.Content.ReadAsStringAsync();
                var similarTracksResponse = JsonConvert.DeserializeObject<SimilarTracksResponse>(content);

                var songs = new List<Song>();

                foreach (var track in similarTracksResponse?.SimilarTracks?.TrackList?.Take(limit) ?? Enumerable.Empty<SimilarTrack>())
                {
                    var song = new Song
                    {
                        Title = track.Name,
                        Artist = track.Artist.Name,
                        Length = 0 // Duration may not be available
                    };

                    // Fetch the album image, or artist image if album image is not available
                    song.ArtworkUrl = await GetAlbumImageUrlAsync(track.Artist.Name, track.Name);

                    songs.Add(song);
                }

                // Add to cache
                _cache.Set(cacheKey, songs, _cacheOptions);
                Debug.WriteLine($"Cached Similar Tracks for '{artistName} - {trackName}'.");

                return songs;
            }
            else
            {
                string? errorContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Error fetching similar tracks for {trackName} by {artistName}: {response.StatusCode}, Content: {errorContent}");
                return Enumerable.Empty<Song>();
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Searches for album images by track name if the initial fetch fails.
        /// </summary>
        private async Task<string> GetAlbumImageUrlByTrackSearchAsync(string trackName)
        {
            int searchLimit = 5; // Limit the number of tracks to check
            string? url = $"?method=track.search&track={Uri.EscapeDataString(trackName)}&api_key={_apiKey}&format=json&limit={searchLimit}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                string? content = await response.Content.ReadAsStringAsync();
                var trackSearchResponse = JsonConvert.DeserializeObject<TrackSearchResponse>(content);

                foreach (var track in trackSearchResponse?.Results?.TrackMatches?.TrackList ?? Enumerable.Empty<TrackSearchResult>())
                {
                    string? artistName = track.Artist;
                    string? trackTitle = track.Name;

                    string? imageUrl = await GetAlbumImageFromTrackAsync(artistName, trackTitle);

                    if (!string.IsNullOrEmpty(imageUrl) && !IsPlaceholderImage(imageUrl))
                    {
                        return imageUrl;
                    }
                }
            }
            else
            {
                string? errorContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Error searching for tracks with name {trackName}: {response.StatusCode}, Content: {errorContent}");
            }

            return null;
        }

        /// <summary>
        /// Retrieves the album image URL from a specific track.
        /// </summary>
        private async Task<string> GetAlbumImageFromTrackAsync(string artistName, string trackName)
        {
            string? url = $"?method=track.getInfo&api_key={_apiKey}&artist={Uri.EscapeDataString(artistName)}&track={Uri.EscapeDataString(trackName)}&format=json";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                string? content = await response.Content.ReadAsStringAsync();
                var trackInfoResponse = JsonConvert.DeserializeObject<TrackInfoResponse>(content);

                var album = trackInfoResponse?.Track?.Album;
                string? imageUrl = album?.Images?.FirstOrDefault(img => img.Size == "large")?.Url
                    ?? album?.Images?.FirstOrDefault(img => img.Size == "medium")?.Url;

                if (!string.IsNullOrEmpty(imageUrl) && !IsPlaceholderImage(imageUrl))
                {
                    return imageUrl;
                }
            }
            else
            {
                string? errorContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Error fetching track info for {trackName} by {artistName}: {response.StatusCode}, Content: {errorContent}");
            }

            return null;
        }

        private async Task<string> GetImageFromCacheAsync(string imageUrl)
        {
            // Create a unique filename based on the URL
            string fileName = Path.GetFileName(imageUrl);
            string cacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundHavenClient", "ImageCache");

            if (!Directory.Exists(cacheDirectory))
            {
                Directory.CreateDirectory(cacheDirectory);
            }

            string filePath = Path.Combine(cacheDirectory, fileName);

            if (File.Exists(filePath))
            {
                // Load from cache
                return filePath;
            }
            else
            {
                // Download the image
                using (var client = new HttpClient())
                {
                    byte[]? imageBytes = await client.GetByteArrayAsync(imageUrl);
                    await File.WriteAllBytesAsync(filePath, imageBytes);
                    return filePath;
                }
            }
        }

        #endregion

    }
}
