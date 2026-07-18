using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace SoundHaven.Services;

/// <summary>A track of a catalog-resolved album.</summary>
public sealed record AlbumTrack(string Title, string? Artist, TimeSpan Duration, int? Year);

/// <summary>An album resolved as one catalog entity: the cover and the track list
/// come from the same release, so they can never disagree.</summary>
public sealed record ResolvedAlbum(string? CoverUrl, IReadOnlyList<AlbumTrack> Tracks);

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

    /// <summary>
    /// Resolve an album's cover and track list from one catalog release (Deezer
    /// first, iTunes fallback), validated against the requested artist and title.
    /// Null when no convincingly matching album exists.
    /// </summary>
    Task<ResolvedAlbum?> GetAlbumWithTracksAsync(
        string? artist,
        string? album,
        CancellationToken cancellationToken = default);

    /// <summary>Resolve a track's release year, or null when unavailable.</summary>
    Task<int?> GetTrackYearAsync(
        string? artist,
        string? title,
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
    private const string DeezerAlbumTracksEndpointBase = "https://api.deezer.com/album";
    private const string ITunesEndpoint = "https://itunes.apple.com/search";
    private const string ITunesLookupEndpoint = "https://itunes.apple.com/lookup";

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

    public async Task<int?> GetTrackYearAsync(
        string? artist,
        string? title,
        CancellationToken cancellationToken = default)
    {
        string normalizedArtist = artist?.Trim() ?? string.Empty;
        string normalizedTitle = title?.Trim() ?? string.Empty;
        if (normalizedTitle.Length == 0)
        {
            return null;
        }

        string cacheKey = FormattableString.Invariant(
            $"trackyear:{normalizedArtist}|{normalizedTitle}")
            .ToLowerInvariant();
        if (_cache.TryGetValue(cacheKey, out int cachedYear))
        {
            return cachedYear > 0 ? cachedYear : null;
        }

        int? year = await QueryITunesReleaseYearAsync(
                normalizedArtist,
                normalizedTitle,
                cancellationToken)
            .ConfigureAwait(false);

        // Cache misses too (as 0) so we don't re-query hopeless songs.
        _cache.Set(cacheKey, year ?? 0, CacheDuration);
        return year;
    }

    private async Task<int?> QueryITunesReleaseYearAsync(
        string artist,
        string title,
        CancellationToken cancellationToken)
    {
        string term = artist.Length > 0 ? $"{artist} {title}" : title;
        string url =
            $"{ITunesEndpoint}?term={Uri.EscapeDataString(term)}&media=music&entity=song&limit=1";

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

            string? releaseDate = GetFirstString(results[0], "releaseDate");
            if (releaseDate is not null
                && DateTimeOffset.TryParse(
                    releaseDate,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset parsed))
            {
                return parsed.Year;
            }

            return null;
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

    public async Task<ResolvedAlbum?> GetAlbumWithTracksAsync(
        string? artist,
        string? album,
        CancellationToken cancellationToken = default)
    {
        string normalizedArtist = artist?.Trim() ?? string.Empty;
        string normalizedAlbum = album?.Trim() ?? string.Empty;
        if (normalizedAlbum.Length == 0)
        {
            return null;
        }

        string cacheKey = FormattableString.Invariant(
            $"albumfull:{normalizedArtist}|{normalizedAlbum}")
            .ToLowerInvariant();
        if (_cache.TryGetValue(cacheKey, out ResolvedAlbum? cached))
        {
            return cached;
        }

        ResolvedAlbum? resolved = await QueryDeezerAlbumWithTracksAsync(
                normalizedArtist,
                normalizedAlbum,
                cancellationToken)
            .ConfigureAwait(false)
            ?? await QueryITunesAlbumWithTracksAsync(
                normalizedArtist,
                normalizedAlbum,
                cancellationToken)
            .ConfigureAwait(false);

        // Cache misses too (as null) so an album page revisit doesn't re-query.
        _cache.Set(cacheKey, resolved, CacheDuration);
        return resolved;
    }

    private async Task<ResolvedAlbum?> QueryDeezerAlbumWithTracksAsync(
        string artist,
        string album,
        CancellationToken cancellationToken)
    {
        string query = artist.Length > 0
            ? $"artist:\"{artist}\" album:\"{album}\""
            : album;
        string url = $"{DeezerAlbumEndpoint}?q={Uri.EscapeDataString(query)}&limit=8&output=json";

        try
        {
            using JsonDocument? document = await FetchJsonAsync(url, cancellationToken)
                .ConfigureAwait(false);
            if (document is null
                || !document.RootElement.TryGetProperty("data", out JsonElement data)
                || data.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            JsonElement? match = PickAlbumMatch(
                data,
                artist,
                album,
                item => item.TryGetProperty("artist", out JsonElement itemArtist)
                    && itemArtist.ValueKind == JsonValueKind.Object
                        ? GetFirstString(itemArtist, "name")
                        : null,
                item => GetFirstString(item, "title"));
            if (match is not JsonElement chosen
                || !chosen.TryGetProperty("id", out JsonElement idElement)
                || !idElement.TryGetInt64(out long albumId))
            {
                return null;
            }

            string? cover = GetFirstString(chosen, "cover_xl", "cover_big", "cover_medium", "cover");

            string tracksUrl = FormattableString.Invariant(
                $"{DeezerAlbumTracksEndpointBase}/{albumId}/tracks?limit=100&output=json");
            using JsonDocument? tracksDocument = await FetchJsonAsync(tracksUrl, cancellationToken)
                .ConfigureAwait(false);
            if (tracksDocument is null
                || !tracksDocument.RootElement.TryGetProperty("data", out JsonElement trackData)
                || trackData.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var tracks = new List<AlbumTrack>();
            foreach (JsonElement trackElement in trackData.EnumerateArray())
            {
                string? title = GetFirstString(trackElement, "title");
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                string? trackArtist = trackElement.TryGetProperty("artist", out JsonElement artistElement)
                    && artistElement.ValueKind == JsonValueKind.Object
                        ? GetFirstString(artistElement, "name")
                        : null;
                TimeSpan duration = trackElement.TryGetProperty("duration", out JsonElement durationElement)
                    && durationElement.TryGetInt32(out int seconds)
                    && seconds > 0
                        ? TimeSpan.FromSeconds(seconds)
                        : TimeSpan.Zero;

                tracks.Add(new AlbumTrack(title, trackArtist, duration, null));
            }

            return tracks.Count > 0 ? new ResolvedAlbum(cover, tracks) : null;
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

    private async Task<ResolvedAlbum?> QueryITunesAlbumWithTracksAsync(
        string artist,
        string album,
        CancellationToken cancellationToken)
    {
        string term = artist.Length > 0 ? $"{artist} {album}" : album;
        string url =
            $"{ITunesEndpoint}?term={Uri.EscapeDataString(term)}&media=music&entity=album&limit=8";

        try
        {
            using JsonDocument? document = await FetchJsonAsync(url, cancellationToken)
                .ConfigureAwait(false);
            if (document is null
                || !document.RootElement.TryGetProperty("results", out JsonElement results)
                || results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            JsonElement? match = PickAlbumMatch(
                results,
                artist,
                album,
                item => GetFirstString(item, "artistName"),
                item => GetFirstString(item, "collectionName"));
            if (match is not JsonElement chosen
                || !chosen.TryGetProperty("collectionId", out JsonElement idElement)
                || !idElement.TryGetInt64(out long collectionId))
            {
                return null;
            }

            string? cover = UpgradeITunesArtworkUrl(
                GetFirstString(chosen, "artworkUrl100", "artworkUrl60"));

            string lookupUrl = $"{ITunesLookupEndpoint}?id={collectionId}&entity=song&limit=200";
            using JsonDocument? lookupDocument = await FetchJsonAsync(lookupUrl, cancellationToken)
                .ConfigureAwait(false);
            if (lookupDocument is null
                || !lookupDocument.RootElement.TryGetProperty("results", out JsonElement lookupResults)
                || lookupResults.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var tracks = new List<(int Disc, int Number, AlbumTrack Track)>();
            foreach (JsonElement item in lookupResults.EnumerateArray())
            {
                if (!string.Equals(GetFirstString(item, "wrapperType"), "track", StringComparison.Ordinal))
                {
                    continue;
                }

                string? title = GetFirstString(item, "trackName");
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                TimeSpan duration = item.TryGetProperty("trackTimeMillis", out JsonElement millisElement)
                    && millisElement.TryGetInt64(out long millis)
                    && millis > 0
                        ? TimeSpan.FromMilliseconds(millis)
                        : TimeSpan.Zero;

                int? year = null;
                string? releaseDate = GetFirstString(item, "releaseDate");
                if (releaseDate is not null
                    && DateTimeOffset.TryParse(
                        releaseDate,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out DateTimeOffset parsed))
                {
                    year = parsed.Year;
                }

                int disc = item.TryGetProperty("discNumber", out JsonElement discElement)
                    && discElement.TryGetInt32(out int discNumber)
                        ? discNumber
                        : 1;
                int number = item.TryGetProperty("trackNumber", out JsonElement numberElement)
                    && numberElement.TryGetInt32(out int trackNumber)
                        ? trackNumber
                        : int.MaxValue;

                tracks.Add((disc, number, new AlbumTrack(
                    title,
                    GetFirstString(item, "artistName"),
                    duration,
                    year)));
            }

            return tracks.Count > 0
                ? new ResolvedAlbum(
                    cover,
                    tracks.OrderBy(entry => entry.Disc)
                        .ThenBy(entry => entry.Number)
                        .Select(entry => entry.Track)
                        .ToList())
                : null;
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

    // Catalog searches are fuzzy, so only accept a result whose artist matches and
    // whose title is the requested album — an exact title wins over a partial one,
    // so "MUSIC" never resolves to "Music Is My Drug" when both are returned.
    private static JsonElement? PickAlbumMatch(
        JsonElement items,
        string requestedArtist,
        string requestedAlbum,
        Func<JsonElement, string?> getArtist,
        Func<JsonElement, string?> getTitle)
    {
        JsonElement? partialMatch = null;
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (!NamesOverlap(getArtist(item), requestedArtist))
            {
                continue;
            }

            string? title = getTitle(item);
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            if (NormalizeName(title) == NormalizeName(requestedAlbum))
            {
                return item;
            }

            if (partialMatch is null && NamesOverlap(title, requestedAlbum))
            {
                partialMatch = item;
            }
        }

        return partialMatch;
    }

    private static string NormalizeName(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    private static bool NamesOverlap(string? candidate, string? requested)
    {
        string normalizedRequested = NormalizeName(requested);
        if (normalizedRequested.Length == 0)
        {
            return true;
        }

        string normalizedCandidate = NormalizeName(candidate);
        return normalizedCandidate.Length > 0
            && (normalizedCandidate.Contains(normalizedRequested, StringComparison.Ordinal)
                || normalizedRequested.Contains(normalizedCandidate, StringComparison.Ordinal));
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
