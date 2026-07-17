using System;
using System.Collections.Generic;
using SoundHaven.Models;

namespace SoundHaven.Helpers;

/// <summary>
/// Interleaves YouTube Music and Last.fm recommendation seeds into one Home shelf.
/// </summary>
public static class RecommendationFeed
{
    public const int MaxYtmSeeds = 12;
    public const int MaxLastFmSeeds = 12;
    public const int MaxDisplay = 18;

    public static IReadOnlyList<Song> MergeInterleaved(
        IEnumerable<Song> youTubeSongs,
        IEnumerable<Song> lastFmSongs,
        int maxDisplay = MaxDisplay)
    {
        ArgumentNullException.ThrowIfNull(youTubeSongs);
        ArgumentNullException.ThrowIfNull(lastFmSongs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDisplay);

        var ytm = TakeValid(youTubeSongs, MaxYtmSeeds);
        var lastFm = TakeValid(lastFmSongs, MaxLastFmSeeds);
        var merged = new List<Song>(maxDisplay);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int ytmIndex = 0;
        int lastFmIndex = 0;
        bool preferYtm = true;

        while (merged.Count < maxDisplay
               && (ytmIndex < ytm.Count || lastFmIndex < lastFm.Count))
        {
            Song? next = null;
            if (preferYtm && ytmIndex < ytm.Count)
            {
                next = ytm[ytmIndex++];
            }
            else if (!preferYtm && lastFmIndex < lastFm.Count)
            {
                next = lastFm[lastFmIndex++];
            }
            else if (ytmIndex < ytm.Count)
            {
                next = ytm[ytmIndex++];
            }
            else if (lastFmIndex < lastFm.Count)
            {
                next = lastFm[lastFmIndex++];
            }

            preferYtm = !preferYtm;

            if (next is null)
            {
                continue;
            }

            string key = BuildDedupeKey(next);
            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
            {
                continue;
            }

            merged.Add(next);
        }

        return merged;
    }

    public static string BuildDedupeKey(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        return $"{song.Title?.Trim()}|{song.Artist?.Trim()}";
    }

    private static List<Song> TakeValid(IEnumerable<Song> source, int limit)
    {
        var list = new List<Song>(limit);
        foreach (Song song in source)
        {
            if (string.IsNullOrWhiteSpace(song.Title))
            {
                continue;
            }

            list.Add(song);
            if (list.Count >= limit)
            {
                break;
            }
        }

        return list;
    }
}
