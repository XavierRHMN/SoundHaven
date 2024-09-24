// Services/LastFmDataService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SoundHeaven.Models;

namespace SoundHeaven.Services
{
    public class LastFmDataService : IDataService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        // Caches to store fetched data and minimize API calls
        private readonly Dictionary<string, string> _albumImageCache = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _artistImageCache = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _userTopArtistsCache = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _artistTopTracksCache = new Dictionary<string, string>();

        public LastFmDataService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://ws.audioscrobbler.com/2.0/")
            };
        }

        #region Top Tracks

        /// <summary>
        /// Fetches the top tracks globally.
        /// </summary>
        public async Task<IEnumerable<Song>> GetTopTracksAsync()
        {
            var limit = 20; // Limit to 20 top tracks
            var url = $"?method=chart.gettoptracks&api_key={_apiKey}&format=json&limit={limit}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var topTracksResponse = JsonConvert.DeserializeObject<TopTracksResponse>(content);

                var songs = new List<Song>();

                foreach (var track in topTracksResponse?.Tracks?.TrackList?.Take(limit) ?? Enumerable.Empty<LastFmTrack>())
                {
                    var song = new Song
                    {
                        Title = track.Name,
                        Artist = track.Artist.Name,
                        Length = int.TryParse(track.Duration, out var duration) ? duration : 0
                    };

                    // Fetch the album image, or artist image if album image is not available
                    song.ArtworkUrl = await GetAlbumImageUrlAsync(track.Artist.Name, track.Name);

                    // Log the artwork URL for debugging
                    Console.WriteLine($"Artwork URL for {song.Title} by {song.Artist}: {song.ArtworkUrl}");

                    songs.Add(song);
                }

                return songs;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error fetching top tracks: {response.StatusCode}, Content: {errorContent}");
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

            var cacheKey = $"user-top-artists-{username}";
            if (_userTopArtistsCache.TryGetValue(cacheKey, out var cachedArtists))
            {
                Console.WriteLine($"Using cached top artists for user '{username}'.");
                return cachedArtists.Split(',');
            }

            var url = $"?method=user.gettopartists&user={Uri.EscapeDataString(username)}&api_key={_apiKey}&format=json&limit={limit}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var userTopArtistsResponse = JsonConvert.DeserializeObject<UserTopArtistsResponse>(content);

                var topArtists = userTopArtistsResponse?.TopArtists?.ArtistList
                    .Select(a => a.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();

                if (topArtists != null && topArtists.Any())
                {
                    _userTopArtistsCache[cacheKey] = string.Join(",", topArtists);
                    Console.WriteLine($"Fetched and cached top artists for user '{username}'.");
                    return topArtists;
                }
                else
                {
                    Console.WriteLine($"No top artists found for user '{username}'.");
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error fetching top artists for user {username}: {response.StatusCode}, Content: {errorContent}");
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
                Console.WriteLine("Artist name is null or empty.");
                return Enumerable.Empty<Song>();
            }

            var cacheKey = $"artist-top-tracks-{artistName}";
            if (_artistTopTracksCache.TryGetValue(cacheKey, out var cachedTracksJson))
            {
                Console.WriteLine($"Using cached top tracks for artist '{artistName}'.");
                return JsonConvert.DeserializeObject<List<Song>>(cachedTracksJson);
            }

            var url = $"?method=artist.gettoptracks&artist={Uri.EscapeDataString(artistName)}&api_key={_apiKey}&format=json&limit={limit}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var topTracksResponse = JsonConvert.DeserializeObject<ArtistTopTracksResponse>(content);

                var songs = topTracksResponse?.TopTracks?.TrackList
                    .Select(track => new Song
                    {
                        Title = track.Name,
                        Artist = artistName,
                        Length = int.TryParse(track.Duration, out var duration) ? duration : 0,
                        ArtworkUrl = null // Optionally fetch artwork here or use existing methods
                    })
                    .Where(song => song != null)
                    .ToList();

                if (songs != null && songs.Any())
                {
                    _artistTopTracksCache[cacheKey] = JsonConvert.SerializeObject(songs);
                    Console.WriteLine($"Fetched and cached top tracks for artist '{artistName}'.");
                    return songs;
                }
                else
                {
                    Console.WriteLine($"No top tracks found for artist '{artistName}'.");
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error fetching top tracks for {artistName}: {response.StatusCode}, Content: {errorContent}");
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
            var maxRecommended = 20;
            var topArtistsLimit = 5; // Number of top artists to fetch
            var topTracksPerArtist = 4; // Number of top tracks to fetch per artist

            // Step 1: Fetch user's top artists
            var topArtists = await GetUserTopArtistsAsync(username, topArtistsLimit);

            foreach (var artist in topArtists)
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

            Console.WriteLine($"Generated {recommendedTracks.Count} recommended tracks for user '{username}'.");
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

            var limit = 20; // Limit to 20 recent tracks
            var url = $"?method=user.getrecenttracks&user={Uri.EscapeDataString(username)}&api_key={_apiKey}&format=json&limit={limit}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
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

                return songs;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error fetching recent tracks for user {username}: {response.StatusCode}, Content: {errorContent}");
                return Enumerable.Empty<Song>();
            }
        }

        #endregion

        #region Similar Artists (Optional)

        // If you still want to include similar artists as an additional recommendation strategy

        /// <summary>
        /// Fetches similar artists for a given artist.
        /// </summary>
        private async Task<IEnumerable<string>> GetSimilarArtistsAsync(string artistName, int limit = 5)
        {
            if (string.IsNullOrEmpty(artistName))
            {
                Console.WriteLine("Artist name is null or empty.");
                return Enumerable.Empty<string>();
            }

            var cacheKey = $"similar-artists-{artistName}";
            if (_artistImageCache.TryGetValue(cacheKey, out var cachedSimilarArtists))
            {
                return cachedSimilarArtists.Split(',');
            }

            var url = $"?method=artist.getsimilar&artist={Uri.EscapeDataString(artistName)}&api_key={_apiKey}&format=json&limit={limit}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var similarArtistsResponse = JsonConvert.DeserializeObject<SimilarArtistsResponse>(content);

                var similarArtists = similarArtistsResponse?.SimilarArtists?.ArtistList
                    .Select(a => a.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();

                if (similarArtists != null && similarArtists.Any())
                {
                    _artistImageCache[cacheKey] = string.Join(",", similarArtists);
                    return similarArtists;
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error fetching similar artists for {artistName}: {response.StatusCode}, Content: {errorContent}");
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
                Console.WriteLine("Artist name or track name is null or empty.");
                return "https://example.com/default-artwork.png"; // Replace with your default image URL
            }

            var cacheKey = $"{artistName}-{trackName}";

            if (_albumImageCache.TryGetValue(cacheKey, out var cachedImageUrl))
            {
                return cachedImageUrl;
            }

            var url = $"?method=track.getInfo&api_key={_apiKey}&artist={Uri.EscapeDataString(artistName)}&track={Uri.EscapeDataString(trackName)}&format=json";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();

                var trackInfoResponse = JsonConvert.DeserializeObject<TrackInfoResponse>(content);

                var album = trackInfoResponse?.Track?.Album;

                var imageUrl = album?.Images?.FirstOrDefault(img => img.Size == "large")?.Url
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

                if (string.IsNullOrEmpty(imageUrl))
                {
                    // Set to default image
                    imageUrl = "https://example.com/default-artwork.png"; // Replace with your default image URL
                }

                _albumImageCache[cacheKey] = imageUrl;

                return imageUrl;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error fetching album info for {trackName} by {artistName}: {response.StatusCode}, Content: {errorContent}");
                return "https://example.com/default-artwork.png"; // Replace with your default image URL
            }
        }

        /// <summary>
        /// Retrieves the album image URL from a specific track.
        /// </summary>
        private async Task<string> GetAlbumImageFromTrackAsync(string artistName, string trackName)
        {
            var url = $"?method=track.getInfo&api_key={_apiKey}&artist={Uri.EscapeDataString(artistName)}&track={Uri.EscapeDataString(trackName)}&format=json";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var trackInfoResponse = JsonConvert.DeserializeObject<TrackInfoResponse>(content);

                var album = trackInfoResponse?.Track?.Album;
                var imageUrl = album?.Images?.FirstOrDefault(img => img.Size == "large")?.Url
                    ?? album?.Images?.FirstOrDefault(img => img.Size == "medium")?.Url;

                if (!string.IsNullOrEmpty(imageUrl) && !IsPlaceholderImage(imageUrl))
                {
                    return imageUrl;
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error fetching track info for {trackName} by {artistName}: {response.StatusCode}, Content: {errorContent}");
            }

            return null;
        }

        /// <summary>
        /// Retrieves the artist's image URL.
        /// </summary>
        public async Task<string> GetArtistImageUrlAsync(string artistName)
        {
            if (string.IsNullOrEmpty(artistName))
            {
                Console.WriteLine("Artist name is null or empty.");
                return null;
            }

            if (_artistImageCache.TryGetValue(artistName, out var cachedImageUrl))
            {
                Console.WriteLine($"Using cached artist image for '{artistName}': {cachedImageUrl}");
                return cachedImageUrl;
            }

            string[] artistNameVariants = new string[]
            {
                artistName,
                artistName.Replace(",", ""), // Remove commas
                artistName.Replace(",", "").Replace("'", ""), // Remove commas and apostrophes
                artistName.ToLower(), // Lowercase
                artistName.ToUpper(), // Uppercase
                // Add more variants if necessary
            };

            foreach (var nameVariant in artistNameVariants)
            {
                var encodedArtistName = Uri.EscapeDataString(nameVariant);
                var url = $"?method=artist.getinfo&artist={encodedArtistName}&api_key={_apiKey}&format=json";

                Console.WriteLine($"Requesting artist info from URL: {_httpClient.BaseAddress}{url}");

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    var artistInfoResponse = JsonConvert.DeserializeObject<ArtistInfoResponse>(content);

                    if (artistInfoResponse?.Error != 0 || artistInfoResponse?.Artist == null)
                    {
                        Console.WriteLine($"API Error or no artist data for '{nameVariant}': {artistInfoResponse?.Message}");
                        continue;
                    }

                    var imageUrl = artistInfoResponse.Artist.Images?
                        .FirstOrDefault(img => img.Size == "extralarge")?.Url
                        ?? artistInfoResponse.Artist.Images?.FirstOrDefault(img => img.Size == "large")?.Url
                        ?? artistInfoResponse.Artist.Images?.FirstOrDefault(img => img.Size == "medium")?.Url
                        ?? artistInfoResponse.Artist.Images?.FirstOrDefault(img => img.Size == "small")?.Url;

                    Console.WriteLine($"Artist image URL for '{nameVariant}': {imageUrl}");

                    if (!string.IsNullOrEmpty(imageUrl) && !IsPlaceholderImage(imageUrl))
                    {
                        _artistImageCache[artistName] = imageUrl;
                        return imageUrl;
                    }
                    else
                    {
                        Console.WriteLine($"No valid image found for '{nameVariant}'. Attempting next variant.");
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Error fetching artist info for '{nameVariant}': {response.StatusCode}, Content: {errorContent}");
                }
            }

            // If no valid image URL found, set to default image or null
            _artistImageCache[artistName] = null;
            Console.WriteLine($"No valid artist image found for '{artistName}'. Setting ArtworkUrl to default.");
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
                "https://lastfm.freetls.fastly.net/i/u/300x300/empty-star.png",
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

        // If you want to retain similar tracks as an additional recommendation source

        /// <summary>
        /// Fetches similar tracks for a given artist and track.
        /// </summary>
        private async Task<IEnumerable<Song>> GetSimilarTracksAsync(string artistName, string trackName, int limit = 10)
        {
            if (string.IsNullOrEmpty(artistName) || string.IsNullOrEmpty(trackName))
            {
                Console.WriteLine("Artist name or track name is null or empty.");
                return Enumerable.Empty<Song>();
            }

            var url = $"?method=track.getsimilar&artist={Uri.EscapeDataString(artistName)}&track={Uri.EscapeDataString(trackName)}&api_key={_apiKey}&format=json&limit={limit}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
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

                return songs;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error fetching similar tracks for {trackName} by {artistName}: {response.StatusCode}, Content: {errorContent}");
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
            var searchLimit = 5; // Limit the number of tracks to check
            var url = $"?method=track.search&track={Uri.EscapeDataString(trackName)}&api_key={_apiKey}&format=json&limit={searchLimit}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var trackSearchResponse = JsonConvert.DeserializeObject<TrackSearchResponse>(content);

                foreach (var track in trackSearchResponse?.Results?.TrackMatches?.TrackList ?? Enumerable.Empty<TrackSearchResult>())
                {
                    var artistName = track.Artist;
                    var trackTitle = track.Name;

                    var imageUrl = await GetAlbumImageFromTrackAsync(artistName, trackTitle);

                    if (!string.IsNullOrEmpty(imageUrl) && !IsPlaceholderImage(imageUrl))
                    {
                        return imageUrl;
                    }
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error searching for tracks with name {trackName}: {response.StatusCode}, Content: {errorContent}");
            }

            return null;
        }

        #endregion
    }
}
