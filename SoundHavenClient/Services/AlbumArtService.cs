using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace SoundHaven.Services;

public interface IAlbumArtService
{
    /// <summary>Resolve a large square cover URL for a track, or null when unavailable.</summary>
    Task<string?> GetTrackArtworkUrlAsync(
        string? artist,
        string? title,
        CancellationToken cancellationToken = default);

    /// <summary>Resolve a large square cover URL for an album, or null when unavailable.</summary>
    Task<string?> GetAlbumArtworkUrlAsync(
        string? artist,
        string? album,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Keyless album-art lookup: Deezer search first (1000px covers), iTunes Search API
/// as fallback (600px). Results (including misses) are memory-cached; all failures
/// soft-fail to null so artwork stays best-effort.
/// </summary>
public sealed class AlbumArtService : IAlbumArtService
{
    private const string DeezerTrackEndpoint = "https://api.deezer.com/search";
    private const string DeezerAlbumEndpoint = "https://api.deezer.com/search/album";
    private const string ITunesEndpoint = "https://itunes.apple.com/search";

    // Last.fm serves this hash as its generic gray-star placeholder image.
    private const string LastFmPlaceholderHash = "2a96cbd8b46e442fc41c2b86b821562f";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(2);

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;

    public AlbumArtService(HttpClient httpClient, IMemoryCache cache)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <summary>
    /// True when the URL points at real artwork rather than nothing or a known
    /// placeholder (e.g. Last.fm's gray star).
    /// </summary>
    public static bool IsUsableArtworkUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url)
            && !url.Contains(LastFmPlaceholderHash, StringComparison.OrdinalIgnoreCase);
    }

    public Task<string?> GetTrackArtworkUrlAsync(
        string? artist,
        string? title,
        CancellationToken cancellationToken = default)
    {
        return ResolveAsync(artist, title, isAlbum: false, cancellationToken);
    }

    public Task<string?> GetAlbumArtworkUrlAsync(
        string? artist,
        string? album,
        CancellationToken cancellationToken = default)
    {
        return ResolveAsync(artist, album, isAlbum: true, cancellationToken);
    }

    private async Task<string?> ResolveAsync(
        string? artist,
        string? name,
        bool isAlbum,
        CancellationToken cancellationToken)
    {
        string normalizedArtist = artist?.Trim() ?? string.Empty;
        string normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length == 0)
        {
            return null;
        }

        string cacheKey = FormattableString.Invariant(
            $"albumart:{(isAlbum ? "album" : "track")}:{normalizedArtist}|{normalizedName}")
            .ToLowerInvariant();
        if (_cache.TryGetValue(cacheKey, out string? cached))
        {
            return string.IsNullOrEmpty(cached) ? null : cached;
        }

        string? resolved = await QueryDeezerAsync(
                normalizedArtist,
                normalizedName,
                isAlbum,
                cancellationToken)
            .ConfigureAwait(false)
            ?? await QueryITunesAsync(
                normalizedArtist,
                normalizedName,
                isAlbum,
                cancellationToken)
            .ConfigureAwait(false);

        // Cache misses too (as empty string) so we don't re-query hopeless songs.
        _cache.Set(cacheKey, resolved ?? string.Empty, CacheDuration);
        return resolved;
    }

    private async Task<string?> QueryDeezerAsync(
        string artist,
        string name,
        bool isAlbum,
        CancellationToken cancellationToken)
    {
        string field = isAlbum ? "album" : "track";
        string query = artist.Length > 0
            ? $"artist:\"{artist}\" {field}:\"{name}\""
            : name;
        string endpoint = isAlbum ? DeezerAlbumEndpoint : DeezerTrackEndpoint;
        string url = $"{endpoint}?q={Uri.EscapeDataString(query)}&limit=1&output=json";

        try
        {
            using JsonDocument? document = await FetchJsonAsync(url, cancellationToken)
                .ConfigureAwait(false);
            if (document is null
                || !document.RootElement.TryGetProperty("data", out JsonElement data)
                || data.ValueKind != JsonValueKind.Array
                || data.GetArrayLength() == 0)
            {
                return null;
            }

            JsonElement first = data[0];
            JsonElement coverSource = first;
            if (!isAlbum)
            {
                if (!first.TryGetProperty("album", out coverSource)
                    || coverSource.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }
            }

            return GetFirstString(coverSource, "cover_xl", "cover_big", "cover_medium", "cover");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> QueryITunesAsync(
        string artist,
        string name,
        bool isAlbum,
        CancellationToken cancellationToken)
    {
        string term = artist.Length > 0 ? $"{artist} {name}" : name;
        string entity = isAlbum ? "album" : "song";
        string url =
            $"{ITunesEndpoint}?term={Uri.EscapeDataString(term)}&media=music&entity={entity}&limit=1";

        try
        {
            using JsonDocument? document = await FetchJsonAsync(url, cancellationToken)
                .ConfigureAwait(false);
            if (document is null
                || !document.RootElement.TryGetProperty("results", out JsonElement results)
                || results.ValueKind != JsonValueKind.Array
                || results.GetArrayLength() == 0)
            {
                return null;
            }

            string? artworkUrl = GetFirstString(results[0], "artworkUrl100", "artworkUrl60");
            return UpgradeITunesArtworkUrl(artworkUrl);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>iTunes thumbnail URLs encode their size; request a 600px variant.</summary>
    internal static string? UpgradeITunesArtworkUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return url
            .Replace("100x100bb", "600x600bb", StringComparison.OrdinalIgnoreCase)
            .Replace("60x60bb", "600x600bb", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<JsonDocument?> FetchJsonAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static string? GetFirstString(JsonElement element, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out JsonElement value)
                && value.ValueKind == JsonValueKind.String)
            {
                string? text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }
}
