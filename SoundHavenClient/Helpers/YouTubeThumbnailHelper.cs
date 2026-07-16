using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SoundHaven.Helpers;

internal static partial class YouTubeThumbnailHelper
{
    public const int PreferredSquareSize = 1200;
    public const int LowQualityPixelThreshold = 400;
    private const int MissingYouTubeThumbByteThreshold = 5_000;

    public static string GetVideoThumbnailUrl(string videoId, string quality = "maxresdefault")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);
        return $"https://i.ytimg.com/vi/{videoId}/{quality}.jpg";
    }

    public static IReadOnlyList<string> GetVideoThumbnailCandidates(string videoId)
    {
        return
        [
            GetVideoThumbnailUrl(videoId, "maxresdefault"),
            GetVideoThumbnailUrl(videoId, "sddefault"),
            GetVideoThumbnailUrl(videoId, "hqdefault")
        ];
    }

    public static string? UpgradeThumbnailUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        Match videoMatch = YtimgVideoThumbRegex().Match(url);
        if (videoMatch.Success)
        {
            return GetVideoThumbnailUrl(videoMatch.Groups["id"].Value);
        }

        if (url.Contains("googleusercontent.com", StringComparison.OrdinalIgnoreCase)
            || url.Contains("ggpht.com", StringComparison.OrdinalIgnoreCase))
        {
            if (GoogleusercontentSizeRegex().IsMatch(url))
            {
                return GoogleusercontentSizeRegex().Replace(
                    url,
                    $"=w{PreferredSquareSize}-h{PreferredSquareSize}");
            }

            if (GoogleusercontentScaledRegex().IsMatch(url))
            {
                return GoogleusercontentScaledRegex().Replace(url, $"=s{PreferredSquareSize}");
            }
        }

        return url;
    }

    public static IEnumerable<string> GetDownloadCandidates(string? thumbnailUrl, string? videoId = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            string? upgraded = UpgradeThumbnailUrl(thumbnailUrl);
            if (!string.IsNullOrWhiteSpace(upgraded) && seen.Add(upgraded))
            {
                yield return upgraded;
            }

            if (seen.Add(thumbnailUrl))
            {
                yield return thumbnailUrl;
            }
        }

        string? resolvedVideoId = videoId;
        if (string.IsNullOrWhiteSpace(resolvedVideoId)
            && !string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            Match match = YtimgVideoThumbRegex().Match(thumbnailUrl);
            if (match.Success)
            {
                resolvedVideoId = match.Groups["id"].Value;
            }
        }

        if (string.IsNullOrWhiteSpace(resolvedVideoId))
        {
            yield break;
        }

        foreach (string candidate in GetVideoThumbnailCandidates(resolvedVideoId))
        {
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    public static bool LooksLikeMissingYouTubeThumbnail(byte[] imageBytes)
    {
        return imageBytes.Length < MissingYouTubeThumbByteThreshold;
    }

    [GeneratedRegex(
        @"^https?://i\.ytimg\.com/vi(?:_webp)?/(?<id>[\w-]{11})/",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex YtimgVideoThumbRegex();

    [GeneratedRegex(
        @"=w\d+-h\d+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GoogleusercontentSizeRegex();

    [GeneratedRegex(
        @"=s\d+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GoogleusercontentScaledRegex();
}
