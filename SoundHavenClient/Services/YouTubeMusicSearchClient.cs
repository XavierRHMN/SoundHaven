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

        string apiKey = ApiKeyHelper.GetYouTubeInnertubeApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "YouTube Music search is not configured. Set the YOUTUBE_INNERTUBE_API_KEY environment variable.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://music.youtube.com/youtubei/v1/search?key={apiKey}&prettyPrint=false");
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

    private static IEnumerable<JsonElement> EnumerateSongItems(JsonElement root)
    {
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
                if (entry.TryGetProperty("musicResponsiveListItemRenderer", out JsonElement item))
                {
                    yield return item;
                }
            }
        }
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
