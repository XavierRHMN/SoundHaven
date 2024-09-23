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
            var url = $"?method=chart.gettoptracks&api_key={_apiKey}&format=json";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var topTracksResponse = JsonConvert.DeserializeObject<TopTracksResponse>(content);

                var songs = new List<Song>();

                foreach (var track in topTracksResponse.Tracks.TrackList)
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

                // Parse the response to extract the album image URL
                var trackInfoResponse = JsonConvert.DeserializeObject<TrackInfoResponse>(content);

                var album = trackInfoResponse?.Track?.Album;

                var imageUrl = album?.Images?.FirstOrDefault(img => img.Size == "large")?.Url;

                // If the large image URL is empty, try medium
                if (string.IsNullOrEmpty(imageUrl))
                {
                    imageUrl = album?.Images?.FirstOrDefault(img => img.Size == "medium")?.Url;
                }

                // If still empty, attempt to fetch the artist's image
                if (string.IsNullOrEmpty(imageUrl))
                {
                    imageUrl = await GetArtistImageUrlAsync(artistName);
                }

                // If still empty, set to null
                if (string.IsNullOrEmpty(imageUrl))
                {
                    imageUrl = null;
                }

                // Cache the image URL (even if null to prevent repeated API calls)
                _albumImageCache[cacheKey] = imageUrl;

                return imageUrl;
            }
            else
            {
                Console.WriteLine($"Error fetching album info for {trackName} by {artistName}: {response.StatusCode}");
                return null;
            }
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
                        .FirstOrDefault(img => img.Size == "mega")?.Url
                        ?? artistInfoResponse.Artist.Images?.FirstOrDefault(img => img.Size == "extralarge")?.Url
                        ?? artistInfoResponse.Artist.Images?.FirstOrDefault(img => img.Size == "large")?.Url;

                    Console.WriteLine($"Artist image URL for '{nameVariant}': {imageUrl}");

                    if (!string.IsNullOrEmpty(imageUrl) && !IsPlaceholderImage(imageUrl))
                    {
                        _artistImageCache[artistName] = imageUrl;
                        return imageUrl;
                    }
                }
                else
                {
                    Console.WriteLine($"Error fetching artist info for '{nameVariant}': {response.StatusCode}");
                }
            }

            // If no valid image URL found, set to null or default image
            _artistImageCache[artistName] = null;
            return null;
        }

        private bool IsPlaceholderImage(string imageUrl)
        {
            var placeholderImageUrls = new HashSet<string>
            {
                "https://lastfm.freetls.fastly.net/i/u/300x300/2a96cbd8b46e442fc41c2b86b821562f.png",
                // Add other known placeholder URLs
            };

            return placeholderImageUrls.Contains(imageUrl);
        }




        public async Task<IEnumerable<Song>> GetRecentlyPlayedTracksAsync(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                throw new ArgumentException("Username cannot be null or empty.", nameof(username));
            }

            var url = $"?method=user.getrecenttracks&user={Uri.EscapeDataString(username)}&api_key={_apiKey}&format=json";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var recentTracksResponse = JsonConvert.DeserializeObject<RecentTracksResponse>(content);

                var songs = new List<Song>();

                foreach (var track in recentTracksResponse.RecentTracks.TrackList)
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

            foreach (var track in topTracks.Take(5)) // Limit the number of top tracks to process
            {
                var similarTracks = await GetSimilarTracksAsync(track.Artist, track.Title);
                recommendedTracks.AddRange(similarTracks);
            }

            // Remove duplicates based on title and artist
            recommendedTracks = recommendedTracks
                .GroupBy(t => new { t.Title, t.Artist })
                .Select(g => g.First())
                .ToList();

            return recommendedTracks;
        }
        
        private async Task<IEnumerable<Song>> GetSimilarTracksAsync(string artistName, string trackName)
        {
            var url = $"?method=track.getsimilar&artist={Uri.EscapeDataString(artistName)}&track={Uri.EscapeDataString(trackName)}&api_key={_apiKey}&format=json";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var similarTracksResponse = JsonConvert.DeserializeObject<SimilarTracksResponse>(content);

                var songs = new List<Song>();

                foreach (var track in similarTracksResponse.SimilarTracks.TrackList)
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
