using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using SoundHaven.Models;

namespace SoundHaven.Services;

public interface ILastFmDataService
{
    /// <summary>Raised after a successful sign-in so views can reload account data.</summary>
    event EventHandler? AuthenticationStateChanged;

    bool IsConfigured { get; }

    bool IsAuthenticated { get; }

    string? LastError { get; }

    string Username { get; }

    Task<IEnumerable<Song>> GetTopTracksAsync(CancellationToken cancellationToken = default);

    Task<IEnumerable<Song>> GetRecentlyPlayedTracksAsync(CancellationToken cancellationToken = default);

    Task<IEnumerable<Song>> GetRecommendedAlbumsAsync(CancellationToken cancellationToken = default);

    Task ScrobbleTrackAsync(string title, string artist, string album);

    Task ScrobbleTrackAsync(
        string title,
        string artist,
        string album,
        CancellationToken cancellationToken);

    Task<bool> UserExistsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed class LastFmDataService : ILastFmDataService, IDisposable
{
    private const string ApiEndpoint = "https://ws.audioscrobbler.com/2.0/";
    private const string ConfigurationError =
        "Last.fm is not configured. Set LASTFM_API_KEY and LASTFM_API_SECRET to enable it.";
    private const string AuthenticationRequiredError =
        "Sign in to Last.fm before using account features.";

    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly MemoryCacheEntryOptions _cacheOptions;
    private readonly SemaphoreSlim _authenticationGate = new(1, 1);
    private string? _sessionKey;
    private int _disposeState;

    public LastFmDataService(
        string apiKey,
        string apiSecret,
        HttpClient httpClient,
        IMemoryCache cache)
    {
        _apiKey = apiKey?.Trim() ?? string.Empty;
        _apiSecret = apiSecret?.Trim() ?? string.Empty;
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _cacheOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromMinutes(30));

        if (!IsConfigured)
        {
            LastError = ConfigurationError;
        }
    }

    public event EventHandler? AuthenticationStateChanged;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_apiKey)
        && !string.IsNullOrWhiteSpace(_apiSecret);

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_sessionKey);

    public string? LastError { get; private set; }

    public string Username { get; private set; } = string.Empty;

    public Task ScrobbleTrackAsync(string title, string artist, string album)
    {
        return ScrobbleTrackAsync(title, artist, album, CancellationToken.None);
    }

    public async Task ScrobbleTrackAsync(
        string title,
        string artist,
        string album,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            LastError = ConfigurationError;
            return;
        }

        if (string.IsNullOrWhiteSpace(_sessionKey))
        {
            LastError = AuthenticationRequiredError;
            return;
        }

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
        {
            LastError = "A track title and artist are required to scrobble.";
            return;
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "track.scrobble",
            ["artist"] = artist,
            ["track"] = title,
            ["album"] = album ?? string.Empty,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture),
            ["sk"] = _sessionKey
        };

        try
        {
            using JsonDocument response = await PostSignedAsync(parameters, cancellationToken);
            LastError = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            LastError = "Last.fm could not scrobble the track.";
        }
    }

    public async Task<bool> UserExistsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            LastError = ConfigurationError;
            return false;
        }

        string normalizedUsername = username?.Trim() ?? string.Empty;
        if (normalizedUsername.Length == 0 || string.IsNullOrEmpty(password))
        {
            LastError = "Username and password are required.";
            return false;
        }

        await _authenticationGate.WaitAsync(cancellationToken);
        try
        {
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["method"] = "auth.getMobileSession",
                ["username"] = normalizedUsername,
                ["password"] = password
            };

            using JsonDocument response = await PostSignedAsync(parameters, cancellationToken);
            if (!response.RootElement.TryGetProperty("session", out JsonElement session)
                || !session.TryGetProperty("key", out JsonElement keyElement))
            {
                LastError = "Last.fm returned an invalid authentication response.";
                return false;
            }

            string? sessionKey = keyElement.GetString();
            if (string.IsNullOrWhiteSpace(sessionKey))
            {
                LastError = "Last.fm did not provide a session.";
                return false;
            }

            _sessionKey = sessionKey;
            Username = session.TryGetProperty("name", out JsonElement nameElement)
                ? nameElement.GetString() ?? normalizedUsername
                : normalizedUsername;
            LastError = null;
            AuthenticationStateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (LastFmApiException exception)
        {
            LastError = exception.Message;
            return false;
        }
        catch (Exception)
        {
            LastError = "Last.fm sign-in is currently unavailable.";
            return false;
        }
        finally
        {
            _authenticationGate.Release();
        }
    }

    public Task<IEnumerable<Song>> GetTopTracksAsync(
        CancellationToken cancellationToken = default)
    {
        return GetTrackCollectionAsync(
            "user.gettoptracks",
            "toptracks",
            "Last.fm top tracks are currently unavailable.",
            cancellationToken);
    }

    public Task<IEnumerable<Song>> GetRecentlyPlayedTracksAsync(
        CancellationToken cancellationToken = default)
    {
        return GetTrackCollectionAsync(
            "user.getrecenttracks",
            "recenttracks",
            "Last.fm recently played tracks are currently unavailable.",
            cancellationToken);
    }

    public async Task<IEnumerable<Song>> GetRecommendedAlbumsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanLoadAccountData())
        {
            return Array.Empty<Song>();
        }

        string cacheKey = $"lastfm:{Username}:top-albums";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<Song>? cached)
            && cached is not null)
        {
            return cached;
        }

        try
        {
            using JsonDocument response = await GetAsync(
                "user.gettopalbums",
                cancellationToken);
            var songs = new List<Song>();
            if (TryGetArray(response.RootElement, "topalbums", "album", out JsonElement albums))
            {
                foreach (JsonElement album in albums.EnumerateArray())
                {
                    string title = GetString(album, "name");
                    string artist = GetNestedString(album, "artist", "name");
                    if (title.Length == 0)
                    {
                        continue;
                    }

                    songs.Add(new Song
                    {
                        Title = title,
                        Album = title,
                        Artist = artist,
                        ArtworkUrl = GetArtworkUrl(album),
                        PlayCount = ParseInt32(GetString(album, "playcount"))
                    });
                }
            }

            _cache.Set(cacheKey, songs, _cacheOptions);
            LastError = null;
            return songs;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            LastError = "Last.fm albums are currently unavailable.";
            return Array.Empty<Song>();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _authenticationGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IEnumerable<Song>> GetTrackCollectionAsync(
        string method,
        string collectionProperty,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        if (!CanLoadAccountData())
        {
            return Array.Empty<Song>();
        }

        string cacheKey = $"lastfm:{Username}:{method}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<Song>? cached)
            && cached is not null)
        {
            return cached;
        }

        try
        {
            using JsonDocument response = await GetAsync(method, cancellationToken);
            var songs = new List<Song>();
            if (TryGetArray(
                    response.RootElement,
                    collectionProperty,
                    "track",
                    out JsonElement tracks))
            {
                foreach (JsonElement track in tracks.EnumerateArray())
                {
                    string title = GetString(track, "name");
                    string artist = GetArtist(track);
                    if (title.Length == 0)
                    {
                        continue;
                    }

                    songs.Add(new Song
                    {
                        Title = title,
                        Artist = artist,
                        Album = GetNestedText(track, "album"),
                        ArtworkUrl = GetArtworkUrl(track),
                        PlayCount = ParseInt32(GetString(track, "playcount"))
                    });
                }
            }

            _cache.Set(cacheKey, songs, _cacheOptions);
            LastError = null;
            return songs;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            LastError = failureMessage;
            return Array.Empty<Song>();
        }
    }

    private async Task<JsonDocument> GetAsync(
        string method,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = method,
            ["user"] = Username,
            ["api_key"] = _apiKey,
            ["format"] = "json",
            ["limit"] = "20"
        };
        string query = string.Join(
            "&",
            parameters.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        using HttpResponseMessage response = await _httpClient.GetAsync(
            $"{ApiEndpoint}?{query}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
        ThrowIfApiError(document.RootElement);
        return document;
    }

    private async Task<JsonDocument> PostSignedAsync(
        IDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        parameters["api_key"] = _apiKey;
        parameters["api_sig"] = CreateApiSignature(parameters);
        parameters["format"] = "json";

        using var content = new FormUrlEncodedContent(parameters);
        using HttpResponseMessage response = await _httpClient.PostAsync(
            ApiEndpoint,
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
        ThrowIfApiError(document.RootElement);
        return document;
    }

    [SuppressMessage(
        "Security",
        "CA5351",
        Justification = "The Last.fm API protocol requires MD5 request signatures; this is not password hashing.")]
    private string CreateApiSignature(IEnumerable<KeyValuePair<string, string>> parameters)
    {
        var signature = new StringBuilder();
        foreach (KeyValuePair<string, string> parameter in parameters
                     .Where(pair => pair.Key is not "format" and not "callback")
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            signature.Append(parameter.Key);
            signature.Append(parameter.Value);
        }

        signature.Append(_apiSecret);
        byte[] digest = MD5.HashData(Encoding.UTF8.GetBytes(signature.ToString()));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private bool CanLoadAccountData()
    {
        if (!IsConfigured)
        {
            LastError = ConfigurationError;
            return false;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            LastError = AuthenticationRequiredError;
            return false;
        }

        return true;
    }

    private static void ThrowIfApiError(JsonElement root)
    {
        if (!root.TryGetProperty("error", out _))
        {
            return;
        }

        string message = root.TryGetProperty("message", out JsonElement messageElement)
            ? messageElement.GetString() ?? "Last.fm rejected the request."
            : "Last.fm rejected the request.";
        throw new LastFmApiException(message);
    }

    private static bool TryGetArray(
        JsonElement root,
        string collectionProperty,
        string itemProperty,
        out JsonElement array)
    {
        if (root.TryGetProperty(collectionProperty, out JsonElement collection)
            && collection.TryGetProperty(itemProperty, out array)
            && array.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        array = default;
        return false;
    }

    private static string GetArtist(JsonElement track)
    {
        string name = GetNestedString(track, "artist", "name");
        return name.Length > 0 ? name : GetNestedText(track, "artist");
    }

    private static string GetNestedText(JsonElement element, string propertyName)
    {
        return GetNestedString(element, propertyName, "#text");
    }

    private static string GetNestedString(
        JsonElement element,
        string propertyName,
        string nestedPropertyName)
    {
        if (element.TryGetProperty(propertyName, out JsonElement nested)
            && nested.ValueKind == JsonValueKind.Object)
        {
            return GetString(nested, nestedPropertyName);
        }

        return string.Empty;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string GetArtworkUrl(JsonElement element)
    {
        if (!element.TryGetProperty("image", out JsonElement images)
            || images.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        string artworkUrl = string.Empty;
        foreach (JsonElement image in images.EnumerateArray())
        {
            string candidate = GetString(image, "#text");
            if (candidate.Length > 0)
            {
                artworkUrl = candidate;
            }
        }

        return artworkUrl;
    }

    private static int ParseInt32(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : 0;
    }

    private sealed class LastFmApiException(string message) : Exception(message);
}
