using System;
using System.Collections.Generic;
using SoundHaven.Models;
using SoundHaven.Services;

namespace SoundHaven.Helpers;

/// <summary>
/// Picks the search result that most plausibly IS the requested track. A bare
/// top-result pick mismatches easily ("MUNYUN" resolving to whatever ranked
/// first), so results whose title actually names the requested track win, with
/// duration proximity and artist agreement breaking ties; duration alone is the
/// fallback signal, and search ranking is trusted only as a last resort.
/// </summary>
public static class YouTubeMatchHelper
{
    /// <summary>How many search results the resolvers fetch to choose among.</summary>
    public const int ResolveSearchLimit = 8;

    // A candidate with no usable duration ranks behind any duration-scored one,
    // and a wrong-artist candidate behind every right-artist one.
    private const double UnknownDurationScore = 600;
    private const double WrongArtistPenalty = 10_000;

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

        // Pass 1: results whose title names the requested track. Closest duration
        // wins among them; a mismatched artist only wins when nothing better exists.
        YouTubeSearchResult? bestTitled = null;
        double bestTitledScore = double.MaxValue;
        for (int i = 0; i < results.Count; i++)
        {
            YouTubeSearchResult result = results[i];
            if (!TitlesOverlap(result.Title, song.Title))
            {
                continue;
            }

            double score = expected > TimeSpan.Zero
                && result.Duration is { } duration
                && duration > TimeSpan.Zero
                    ? Math.Abs((duration - expected).TotalSeconds)
                    : UnknownDurationScore;

            if (!ArtistsAgree(result.Author, song.Artist))
            {
                score += WrongArtistPenalty;
            }

            score += i * 0.001; // stable tie-break on search ranking
            if (score < bestTitledScore)
            {
                bestTitledScore = score;
                bestTitled = result;
            }
        }

        if (bestTitled is not null)
        {
            return bestTitled;
        }

        if (expected <= TimeSpan.Zero)
        {
            return results[0];
        }

        // Pass 2: no titled hit — fall back to duration proximity. Live versions
        // and remixes run long; allow some slack, proportionally more for long
        // tracks. Nothing close enough → trust the search ranking.
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

        double tolerance = Math.Max(15, expected.TotalSeconds * 0.25);
        return closest is not null && closestDiff <= tolerance ? closest : results[0];
    }

    // "MUNYUN (Official Audio)" names "MUNYUN"; "Long Time (Intro)" does not.
    private static bool TitlesOverlap(string? candidate, string? requested) =>
        KeysOverlap(candidate, requested);

    // Channel names decorate the artist ("Playboi Carti - Topic", "PlayboiCartiVEVO"),
    // so compare with spacing/punctuation stripped.
    private static bool ArtistsAgree(string? candidateAuthor, string? requestedArtist)
    {
        string normalizedRequested = NormalizeKey(requestedArtist);
        if (normalizedRequested.Length == 0)
        {
            return true;
        }

        string normalizedAuthor = NormalizeKey(candidateAuthor);
        return normalizedAuthor.Length == 0
            || normalizedAuthor.Contains(normalizedRequested, StringComparison.Ordinal)
            || normalizedRequested.Contains(normalizedAuthor, StringComparison.Ordinal);
    }

    /// <summary>Both names non-empty and one contains the other after normalizing.</summary>
    public static bool KeysOverlap(string? first, string? second)
    {
        string normalizedFirst = NormalizeKey(first);
        string normalizedSecond = NormalizeKey(second);
        if (normalizedFirst.Length == 0 || normalizedSecond.Length == 0)
        {
            return false;
        }

        return normalizedFirst.Contains(normalizedSecond, StringComparison.Ordinal)
            || normalizedSecond.Contains(normalizedFirst, StringComparison.Ordinal);
    }

    /// <summary>Lowercase alphanumerics only, so casing, spacing, and punctuation
    /// never block a match.</summary>
    public static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[value.Length];
        int length = 0;
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] = char.ToLowerInvariant(character);
            }
        }

        return new string(buffer[..length]);
    }
}
