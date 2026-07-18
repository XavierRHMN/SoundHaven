using System;
using System.Collections.Generic;
using SoundHaven.Models;
using SoundHaven.Services;

namespace SoundHaven.Helpers;

/// <summary>
/// Picks the search result that most plausibly IS the requested track. A bare
/// top-result pick mismatches easily (a 31-second interlude resolving to some
/// 3-minute song that ranks higher); when the track's duration is known, the
/// closest-duration result within tolerance wins instead.
/// </summary>
public static class YouTubeMatchHelper
{
    /// <summary>How many search results the resolvers fetch to choose among.</summary>
    public const int ResolveSearchLimit = 6;

    public static YouTubeSearchResult? PickBestMatch(
        IReadOnlyList<YouTubeSearchResult> results,
        Song song)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(song);

        if (results.Count == 0)
        {
            return null;
        }

        TimeSpan expected = song.Duration;
        if (expected <= TimeSpan.Zero)
        {
            return results[0];
        }

        YouTubeSearchResult? closest = null;
        double closestDiff = double.MaxValue;
        foreach (YouTubeSearchResult result in results)
        {
            if (result.Duration is not { } duration || duration <= TimeSpan.Zero)
            {
                continue;
            }

            double diff = Math.Abs((duration - expected).TotalSeconds);
            if (diff < closestDiff)
            {
                closestDiff = diff;
                closest = result;
            }
        }

        // Live versions and remixes run long; allow some slack, proportionally
        // more for long tracks. Nothing close enough → trust the search ranking.
        double tolerance = Math.Max(15, expected.TotalSeconds * 0.25);
        return closest is not null && closestDiff <= tolerance ? closest : results[0];
    }
}
