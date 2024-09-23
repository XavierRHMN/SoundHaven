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
        private readonly Dictionary<string, string> _albumImageCache = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _artistImageCache = new Dictionary<string, string>();

        public LastFmDataService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://ws.audioscrobbler.com/2.0/")
            };
        }

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

                foreach (var track in topTracksResponse.Tracks.TrackList.Take(limit))
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
                // Handle errors appropriately
                Console.WriteLine($"Error fetching top tracks: {response.StatusCode}");
                return Enumerable.Empty<Song>();
            }
        }

        public async Task<string> GetAlbumImageUrlAsync(string artistName, string trackName)
        {
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
                Console.WriteLine($"Error fetching album info for {trackName} by {artistName}: {response.StatusCode}");
                return "https://example.com/default-artwork.png"; // Replace with your default image URL
            }
        }

        private async Task<string> GetAlbumImageUrlByTrackSearchAsync(string trackName)
        {
            var searchLimit = 5; // Limit the number of tracks to check
            var url = $"?method=track.search&track={Uri.EscapeDataString(trackName)}&api_key={_apiKey}&format=json&limit={searchLimit}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var trackSearchResponse = JsonConvert.DeserializeObject<TrackSearchResponse>(content);

                foreach (var track in trackSearchResponse.Results.TrackMatches.TrackList)
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
                Console.WriteLine($"Error searching for tracks with name {trackName}: {response.StatusCode}");
            }

            return null;
        }

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
                Console.WriteLine($"Error fetching track info for {trackName} by {artistName}: {response.StatusCode}");
            }

            return null;
        }


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
                    Console.WriteLine($"Error fetching artist info for '{nameVariant}': {response.StatusCode}");
                }
            }

            // If no valid image URL found, set to null or default image
            _artistImageCache[artistName] = null;
            Console.WriteLine($"No valid artist image found for '{artistName}'. Setting ArtworkUrl to null.");
            return null;
        }

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

                foreach (var track in recentTracksResponse.RecentTracks.TrackList.Take(limit))
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
                // Handle errors appropriately
                Console.WriteLine($"Error fetching recent tracks for user {username}: {response.StatusCode}");
                return Enumerable.Empty<Song>();
            }
        }

        public async Task<IEnumerable<Song>> GetRecommendedTracksAsync()
        {
            var topTracks = await GetTopTracksAsync();
            var recommendedTracks = new List<Song>();
            var maxRecommended = 20;
            var perTrackLimit = 4; // To ensure total does not exceed 20 (5 top tracks * 4 similar each)

            foreach (var track in topTracks.Take(maxRecommended / perTrackLimit))
            {
                var similarTracks = await GetSimilarTracksAsync(track.Artist, track.Title, perTrackLimit);
                recommendedTracks.AddRange(similarTracks);
                
                if (recommendedTracks.Count >= maxRecommended)
                    break;
            }

            // Remove duplicates based on title and artist and limit to 20
            recommendedTracks = recommendedTracks
                .GroupBy(t => new { t.Title, t.Artist })
                .Select(g => g.First())
                .Take(maxRecommended)
                .ToList();

            return recommendedTracks;
        }
        
        private async Task<IEnumerable<Song>> GetSimilarTracksAsync(string artistName, string trackName, int limit = 10)
        {
            var url = $"?method=track.getsimilar&artist={Uri.EscapeDataString(artistName)}&track={Uri.EscapeDataString(trackName)}&api_key={_apiKey}&format=json&limit={limit}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var similarTracksResponse = JsonConvert.DeserializeObject<SimilarTracksResponse>(content);

                var songs = new List<Song>();

                foreach (var track in similarTracksResponse.SimilarTracks.TrackList.Take(limit))
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
                // Handle errors appropriately
                Console.WriteLine($"Error fetching similar tracks for {trackName} by {artistName}: {response.StatusCode}");
                return Enumerable.Empty<Song>();
            }
        }
    }
}
