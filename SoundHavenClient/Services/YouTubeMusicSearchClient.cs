using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SoundHaven.Helpers;

namespace SoundHaven.Services;

/// <summary>
/// Lightweight YouTube Music (WEB_REMIX) song search.
/// YoutubeExplode only exposes general YouTube video/playlist/channel search,
/// so song mode talks to music.youtube.com directly and still resolves streams
/// through YoutubeExplode afterwards.
/// </summary>
internal sealed class YouTubeMusicSearchClient
{
    // Public Innertube key used by the YouTube Music web client (not a private developer secret).
    private const string InnertubeApiKey = "AIzaSyC9XL3ZjWddXya6X74dJoCTL-WEYFDNX30";
    private const string ClientVersion = "1.20250317.01.00";
    // Filter params for songs-only catalogue search (from ytmusicapi).
    private const string SongsFilterParams = "EgWKAQIIAWoMEA4QChADEAQQCRAF";

    private readonly HttpClient _httpClient;

    public YouTubeMusicSearchClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<IReadOnlyList<YouTubeSearchResult>> SearchSongsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<YouTubeSearchResult>();
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://music.youtube.com/youtubei/v1/search?key={InnertubeApiKey}&prettyPrint=false");
        request.Headers.TryAddWithoutValidation("Origin", "https://music.youtube.com");
        request.Headers.TryAddWithoutValidation("Referer", "https://music.youtube.com/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = new
        {
            context = new
            {
                client = new
                {
                    clientName = "WEB_REMIX",
                    clientVersion = ClientVersion,
                    hl = "en",
                    gl = "US"
                }
            },
            query = query.Trim(),
            @params = SongsFilterParams
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using JsonDocument document = await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var results = new List<YouTubeSearchResult>(limit);
        foreach (JsonElement item in EnumerateSongItems(document.RootElement))
        {
            if (TryParseSongItem(item, out YouTubeSearchResult? result) && result is not null)
            {
                results.Add(result);
                if (results.Count >= limit)
                {
                    break;
                }
            }
        }

        if (results.Count == 0)
        {
            throw new InvalidOperationException(
                "YouTube Music returned no song results for that query.");
        }

        return results;
    }

    /// <summary>
    /// Guest-friendly YouTube Music home feed songs (browseId FEmusic_home).
    /// Guest home shelves are mostly playlists, so when direct song rows are
    /// missing we expand featured playlist browseIds into playable tracks.
    /// Soft-fails to an empty list when the response cannot be parsed.
    /// </summary>
    public async Task<IReadOnlyList<YouTubeSearchResult>> BrowseHomeSongsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        try
        {
            using JsonDocument? homeDocument = await BrowseAsync("FEmusic_home", cancellationToken)
                .ConfigureAwait(false);
            if (homeDocument is null)
            {
                return Array.Empty<YouTubeSearchResult>();
            }

            var results = new List<YouTubeSearchResult>(limit);
            var seenVideoIds = new HashSet<string>(StringComparer.Ordinal);
            CollectSongsFromDocument(homeDocument.RootElement, results, seenVideoIds, limit);

            if (results.Count >= limit)
            {
                return results;
            }

            foreach (string playlistBrowseId in EnumerateHomePlaylistBrowseIds(homeDocument.RootElement)
                         .Take(3))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using JsonDocument? playlistDocument =
                    await BrowseAsync(playlistBrowseId, cancellationToken).ConfigureAwait(false);
                if (playlistDocument is null)
                {
                    continue;
                }

                CollectSongsFromDocument(playlistDocument.RootElement, results, seenVideoIds, limit);
                if (results.Count >= limit)
                {
                    break;
                }
            }

            return results;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Array.Empty<YouTubeSearchResult>();
        }
    }

    private async Task<JsonDocument?> BrowseAsync(
        string browseId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(browseId))
        {
            return null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://music.youtube.com/youtubei/v1/browse?key={InnertubeApiKey}&prettyPrint=false");
        request.Headers.TryAddWithoutValidation("Origin", "https://music.youtube.com");
        request.Headers.TryAddWithoutValidation("Referer", "https://music.youtube.com/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = new
        {
            context = new
            {
                client = new
                {
                    clientName = "WEB_REMIX",
                    clientVersion = ClientVersion,
                    hl = "en",
                    gl = "US"
                }
            },
            browseId
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using Stream stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static void CollectSongsFromDocument(
        JsonElement root,
        List<YouTubeSearchResult> results,
        HashSet<string> seenVideoIds,
        int limit)
    {
        foreach (JsonElement item in EnumerateHomeSongItems(root))
        {
            if (!TryParseHomeSongItem(item, out YouTubeSearchResult? result)
                || result is null
                || !seenVideoIds.Add(result.VideoId))
            {
                continue;
            }

            results.Add(result);
            if (results.Count >= limit)
            {
                return;
            }
        }
    }

    private static IEnumerable<string> EnumerateHomePlaylistBrowseIds(JsonElement root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("contents", out JsonElement contents))
        {
            yield break;
        }

        foreach (JsonElement shelf in EnumerateMusicShelves(contents))
        {
            if (!shelf.TryGetProperty("contents", out JsonElement shelfContents)
                || shelfContents.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement entry in shelfContents.EnumerateArray())
            {
                if (!entry.TryGetProperty("musicTwoRowItemRenderer", out JsonElement twoRow))
                {
                    continue;
                }

                string? browseId = GetTwoRowPlaylistBrowseId(twoRow);
                if (string.IsNullOrWhiteSpace(browseId) || !seen.Add(browseId))
                {
                    continue;
                }

                yield return browseId;
            }
        }
    }

    private static string? GetTwoRowPlaylistBrowseId(JsonElement twoRow)
    {
        if (TryGetPropertyPath(
                twoRow,
                out JsonElement browseEndpoint,
                "navigationEndpoint",
                "browseEndpoint")
            && browseEndpoint.TryGetProperty("browseId", out JsonElement browseIdElement)
            && browseIdElement.ValueKind == JsonValueKind.String)
        {
            string? browseId = browseIdElement.GetString();
            if (!string.IsNullOrWhiteSpace(browseId)
                && browseId.StartsWith("VL", StringComparison.Ordinal))
            {
                return browseId;
            }
        }

        return null;
    }

    private static IEnumerable<JsonElement> EnumerateSongItems(JsonElement root)
    {
        if (!root.TryGetProperty("contents", out JsonElement contents))
        {
            yield break;
        }

        foreach (JsonElement shelf in EnumerateMusicShelves(contents))
        {
            foreach (JsonElement item in EnumerateShelfSongItems(shelf))
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<JsonElement> EnumerateHomeSongItems(JsonElement root)
    {
        if (!root.TryGetProperty("contents", out JsonElement contents))
        {
            yield break;
        }

        // Playlist browse pages nest tracks under musicPlaylistShelfRenderer /
        // singleColumnBrowseResultsRenderer, which EnumerateMusicShelves covers.
        // Also walk sectionList contents that use musicPlaylistShelfRenderer.
        foreach (JsonElement shelf in EnumerateMusicShelves(contents))
        {
            foreach (JsonElement item in EnumerateShelfSongItems(shelf))
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<JsonElement> EnumerateShelfSongItems(JsonElement shelf)
    {
        if (!shelf.TryGetProperty("contents", out JsonElement shelfContents)
            || shelfContents.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement entry in shelfContents.EnumerateArray())
        {
            if (entry.TryGetProperty("musicResponsiveListItemRenderer", out JsonElement listItem))
            {
                yield return listItem;
            }
            else if (entry.TryGetProperty("musicTwoRowItemRenderer", out JsonElement twoRow))
            {
                yield return twoRow;
            }
        }
    }

    private static bool TryParseHomeSongItem(JsonElement item, out YouTubeSearchResult? result)
    {
        if (TryParseSongItem(item, out result))
        {
            return true;
        }

        return TryParseTwoRowSongItem(item, out result);
    }

    private static bool TryParseTwoRowSongItem(JsonElement item, out YouTubeSearchResult? result)
    {
        result = null;

        string? videoId = GetTwoRowVideoId(item);
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return false;
        }

        string title = GetRunsText(item, "title") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        string[] subtitleParts = GetRunsParts(item, "subtitle");
        string artist = subtitleParts.Length > 0 ? subtitleParts[0] : "Unknown artist";
        string? thumbnailUrl = YouTubeThumbnailHelper.UpgradeThumbnailUrl(GetTwoRowThumbnailUrl(item))
            ?? YouTubeThumbnailHelper.GetVideoThumbnailUrl(videoId);

        result = new YouTubeSearchResult(
            videoId,
            title,
            artist,
            Album: null,
            Duration: null,
            thumbnailUrl,
            ViewCount: 0,
            Year: null);
        return true;
    }

    private static string? GetTwoRowVideoId(JsonElement item)
    {
        if (TryGetPropertyPath(
                item,
                out JsonElement watchEndpoint,
                "navigationEndpoint",
                "watchEndpoint")
            && watchEndpoint.TryGetProperty("videoId", out JsonElement idElement)
            && idElement.ValueKind == JsonValueKind.String)
        {
            return idElement.GetString();
        }

        if (TryGetPropertyPath(
                item,
                out JsonElement overlayWatch,
                "thumbnailOverlay",
                "musicItemThumbnailOverlayRenderer",
                "content",
                "musicPlayButtonRenderer",
                "playNavigationEndpoint",
                "watchEndpoint")
            && overlayWatch.TryGetProperty("videoId", out JsonElement overlayId)
            && overlayId.ValueKind == JsonValueKind.String)
        {
            return overlayId.GetString();
        }

        return GetVideoId(item);
    }

    private static string? GetTwoRowThumbnailUrl(JsonElement item)
    {
        if (TryGetPropertyPath(
                item,
                out JsonElement thumbnails,
                "thumbnailRenderer",
                "musicThumbnailRenderer",
                "thumbnail",
                "thumbnails")
            && thumbnails.ValueKind == JsonValueKind.Array)
        {
            return thumbnails.EnumerateArray()
                .Select(thumbnail =>
                {
                    string? url = thumbnail.TryGetProperty("url", out JsonElement urlElement)
                        ? urlElement.GetString()
                        : null;
                    int width = thumbnail.TryGetProperty("width", out JsonElement widthElement)
                        ? widthElement.GetInt32()
                        : 0;
                    int height = thumbnail.TryGetProperty("height", out JsonElement heightElement)
                        ? heightElement.GetInt32()
                        : 0;
                    return (Url: url, Area: width * height);
                })
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Url))
                .OrderByDescending(candidate => candidate.Area)
                .Select(candidate => candidate.Url)
                .FirstOrDefault();
        }

        return GetBestThumbnailUrl(item);
    }

    private static string? GetRunsText(JsonElement item, string propertyName)
    {
        string[] parts = GetRunsParts(item, propertyName);
        return parts.Length == 0 ? null : string.Join("", parts);
    }

    private static string[] GetRunsParts(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out JsonElement node)
            || !node.TryGetProperty("runs", out JsonElement runs)
            || runs.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return runs.EnumerateArray()
            .Select(run => run.TryGetProperty("text", out JsonElement text) ? text.GetString() : null)
            .Where(text => !string.IsNullOrWhiteSpace(text) && text != " • ")
            .Select(text => text!)
            .ToArray();
    }

    private static IEnumerable<JsonElement> EnumerateMusicShelves(JsonElement contents)
    {
        if (contents.TryGetProperty("tabbedSearchResultsRenderer", out JsonElement tabbed)
            && tabbed.TryGetProperty("tabs", out JsonElement tabs)
            && tabs.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement tab in tabs.EnumerateArray())
            {
                if (tab.TryGetProperty("tabRenderer", out JsonElement tabRenderer)
                    && tabRenderer.TryGetProperty("content", out JsonElement tabContent))
                {
                    foreach (JsonElement shelf in EnumerateShelvesFromSectionList(tabContent))
                    {
                        yield return shelf;
                    }
                }
            }

            yield break;
        }

        if (contents.TryGetProperty("singleColumnBrowseResultsRenderer", out JsonElement browse)
            && browse.TryGetProperty("tabs", out JsonElement browseTabs)
            && browseTabs.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement tab in browseTabs.EnumerateArray())
            {
                if (tab.TryGetProperty("tabRenderer", out JsonElement tabRenderer)
                    && tabRenderer.TryGetProperty("content", out JsonElement tabContent))
                {
                    foreach (JsonElement shelf in EnumerateShelvesFromSectionList(tabContent))
                    {
                        yield return shelf;
                    }
                }
            }

            yield break;
        }

        if (contents.TryGetProperty("twoColumnBrowseResultsRenderer", out JsonElement twoColumn))
        {
            if (twoColumn.TryGetProperty("secondaryContents", out JsonElement secondary)
                && secondary.TryGetProperty("sectionListRenderer", out _))
            {
                foreach (JsonElement shelf in EnumerateShelvesFromSectionList(secondary))
                {
                    yield return shelf;
                }
            }

            if (twoColumn.TryGetProperty("tabs", out JsonElement twoColumnTabs)
                && twoColumnTabs.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement tab in twoColumnTabs.EnumerateArray())
                {
                    if (tab.TryGetProperty("tabRenderer", out JsonElement tabRenderer)
                        && tabRenderer.TryGetProperty("content", out JsonElement tabContent))
                    {
                        foreach (JsonElement shelf in EnumerateShelvesFromSectionList(tabContent))
                        {
                            yield return shelf;
                        }
                    }
                }
            }

            yield break;
        }

        foreach (JsonElement shelf in EnumerateShelvesFromSectionList(contents))
        {
            yield return shelf;
        }
    }

    private static IEnumerable<JsonElement> EnumerateShelvesFromSectionList(JsonElement content)
    {
        if (!TryGetSectionList(content, out JsonElement sectionList)
            || sectionList.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement section in sectionList.EnumerateArray())
        {
            if (section.TryGetProperty("musicShelfRenderer", out JsonElement shelf))
            {
                yield return shelf;
            }
            else if (section.TryGetProperty("musicCarouselShelfRenderer", out JsonElement carousel))
            {
                yield return carousel;
            }
            else if (section.TryGetProperty("musicPlaylistShelfRenderer", out JsonElement playlistShelf))
            {
                yield return playlistShelf;
            }
        }
    }

    private static bool TryGetSectionList(JsonElement content, out JsonElement sectionList)
    {
        if (content.TryGetProperty("sectionListRenderer", out JsonElement sectionListRenderer)
            && sectionListRenderer.TryGetProperty("contents", out sectionList))
        {
            return true;
        }

        sectionList = default;
        return false;
    }

    private static bool TryParseSongItem(JsonElement item, out YouTubeSearchResult? result)
    {
        result = null;

        string? videoId = GetVideoId(item);
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return false;
        }

        string title = GetFlexText(item, columnIndex: 0) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        string[] secondaryParts = GetFlexRuns(item, columnIndex: 1);
        string artist = secondaryParts.Length > 0 ? secondaryParts[0] : "Unknown artist";
        string? album = FindAlbumName(secondaryParts);
        TimeSpan? duration = ParseDuration(
            secondaryParts.Length > 0 ? secondaryParts[^1] : null);
        string? thumbnailUrl = YouTubeThumbnailHelper.UpgradeThumbnailUrl(GetBestThumbnailUrl(item))
            ?? YouTubeThumbnailHelper.GetVideoThumbnailUrl(videoId);

        result = new YouTubeSearchResult(
            videoId,
            title,
            artist,
            album,
            duration,
            thumbnailUrl,
            ViewCount: 0,
            Year: null);
        return true;
    }

    private static string? GetVideoId(JsonElement item)
    {
        if (item.TryGetProperty("playlistItemData", out JsonElement playlistItemData)
            && playlistItemData.TryGetProperty("videoId", out JsonElement directId)
            && directId.ValueKind == JsonValueKind.String)
        {
            return directId.GetString();
        }

        if (TryGetPropertyPath(
                item,
                out JsonElement watchEndpoint,
                "overlay",
                "musicItemThumbnailOverlayRenderer",
                "content",
                "musicPlayButtonRenderer",
                "playNavigationEndpoint",
                "watchEndpoint")
            && watchEndpoint.TryGetProperty("videoId", out JsonElement overlayId)
            && overlayId.ValueKind == JsonValueKind.String)
        {
            return overlayId.GetString();
        }

        return null;
    }

    private static string? GetFlexText(JsonElement item, int columnIndex)
    {
        string[] runs = GetFlexRuns(item, columnIndex);
        return runs.Length == 0 ? null : string.Join("", runs);
    }

    private static string[] GetFlexRuns(JsonElement item, int columnIndex)
    {
        if (!item.TryGetProperty("flexColumns", out JsonElement flexColumns)
            || flexColumns.ValueKind != JsonValueKind.Array
            || flexColumns.GetArrayLength() <= columnIndex)
        {
            return [];
        }

        JsonElement column = flexColumns[columnIndex];
        if (!TryGetPropertyPath(
                column,
                out JsonElement runs,
                "musicResponsiveListItemFlexColumnRenderer",
                "text",
                "runs")
            || runs.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return runs.EnumerateArray()
            .Select(run => run.TryGetProperty("text", out JsonElement text) ? text.GetString() : null)
            .Where(text => !string.IsNullOrWhiteSpace(text) && text != " • ")
            .Select(text => text!)
            .ToArray();
    }

    private static string? FindAlbumName(string[] parts)
    {
        // Typical song subtitle: Artist • Album • Duration
        if (parts.Length >= 3)
        {
            return parts[^2];
        }

        return parts.Length == 2 && !LooksLikeDuration(parts[1]) ? parts[1] : null;
    }

    private static bool LooksLikeDuration(string value)
    {
        return TimeSpan.TryParseExact(
                   value,
                   ["m\\:ss", "mm\\:ss", "h\\:mm\\:ss", "hh\\:mm\\:ss"],
                   CultureInfo.InvariantCulture,
                   out _)
               || value.Count(character => character == ':') is 1 or 2;
    }

    private static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] pieces = value.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length is < 2 or > 3
            || pieces.Any(piece => !int.TryParse(piece, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            return null;
        }

        int[] values = pieces
            .Select(piece => int.Parse(piece, CultureInfo.InvariantCulture))
            .ToArray();
        return values.Length == 2
            ? new TimeSpan(0, values[0], values[1])
            : new TimeSpan(values[0], values[1], values[2]);
    }

    private static string? GetBestThumbnailUrl(JsonElement item)
    {
        if (!TryGetPropertyPath(
                item,
                out JsonElement thumbnails,
                "thumbnail",
                "musicThumbnailRenderer",
                "thumbnail",
                "thumbnails")
            || thumbnails.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return thumbnails.EnumerateArray()
            .Select(thumbnail =>
            {
                string? url = thumbnail.TryGetProperty("url", out JsonElement urlElement)
                    ? urlElement.GetString()
                    : null;
                int width = thumbnail.TryGetProperty("width", out JsonElement widthElement)
                    ? widthElement.GetInt32()
                    : 0;
                int height = thumbnail.TryGetProperty("height", out JsonElement heightElement)
                    ? heightElement.GetInt32()
                    : 0;
                return (Url: url, Area: width * height);
            })
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Url))
            .OrderByDescending(candidate => candidate.Area)
            .Select(candidate => candidate.Url)
            .FirstOrDefault();
    }

    private static bool TryGetPropertyPath(
        JsonElement element,
        out JsonElement value,
        params string[] path)
    {
        value = element;
        foreach (string segment in path)
        {
            if (!value.TryGetProperty(segment, out value))
            {
                return false;
            }
        }

        return true;
    }
}
