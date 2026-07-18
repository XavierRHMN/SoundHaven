using SoundHaven.Helpers;
using SoundHaven.Models;
using SoundHaven.Services;

namespace SoundHaven.Tests;

public sealed class YouTubeMatchHelperTests
{
    private static Song MakeSong(string title, string artist, int durationSeconds) => new()
    {
        Title = title,
        Artist = artist,
        Duration = TimeSpan.FromSeconds(durationSeconds)
    };

    private static YouTubeSearchResult MakeResult(
        string videoId,
        string title,
        string author,
        int? durationSeconds) => new(
        videoId,
        title,
        author,
        null,
        durationSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
        null,
        0,
        null);

    [Fact]
    public void TitledResult_BeatsTopRankedWrongSong()
    {
        // The regression: playing MUNYUN resolved to Long Time because the
        // top-ranked result won on the blind fallback.
        var results = new[]
        {
            MakeResult("wrong", "Long Time (Intro)", "Playboi Carti - Topic", 221),
            MakeResult("right", "MUNYUN", "Playboi Carti - Topic", 114)
        };

        YouTubeSearchResult? match = YouTubeMatchHelper.PickBestMatch(
            results,
            MakeSong("MUNYUN", "Playboi Carti", 113));

        Assert.Equal("right", match?.VideoId);
    }

    [Fact]
    public void TitledResult_WinsEvenWithoutDuration()
    {
        var results = new[]
        {
            MakeResult("wrong", "Long Time (Intro)", "Playboi Carti - Topic", 113),
            MakeResult("right", "MUNYUN (Official Audio)", "PlayboiCartiVEVO", null)
        };

        YouTubeSearchResult? match = YouTubeMatchHelper.PickBestMatch(
            results,
            MakeSong("MUNYUN", "Playboi Carti", 113));

        Assert.Equal("right", match?.VideoId);
    }

    [Fact]
    public void RightArtist_BeatsWrongArtistCoverWithBetterDuration()
    {
        var results = new[]
        {
            MakeResult("cover", "MUNYUN (cover)", "Random Covers", 113),
            MakeResult("right", "MUNYUN", "Playboi Carti - Topic", 118)
        };

        YouTubeSearchResult? match = YouTubeMatchHelper.PickBestMatch(
            results,
            MakeSong("MUNYUN", "Playboi Carti", 113));

        Assert.Equal("right", match?.VideoId);
    }

    [Fact]
    public void NoTitledHit_FallsBackToDurationWithinTolerance()
    {
        // The interlude case: titles all differ, but only one result is ~31s.
        var results = new[]
        {
            MakeResult("wrong", "Some Other Song", "Cult Member - Topic", 210),
            MakeResult("right", "Untitled Skit", "Cult Member - Topic", 33)
        };

        YouTubeSearchResult? match = YouTubeMatchHelper.PickBestMatch(
            results,
            MakeSong("Lil Kim Interlude", "Cult Member", 31));

        Assert.Equal("right", match?.VideoId);
    }

    [Fact]
    public void NothingPlausible_TrustsSearchRanking()
    {
        var results = new[]
        {
            MakeResult("first", "Some Other Song", "Somebody", 300),
            MakeResult("second", "Another Song", "Somebody Else", 290)
        };

        YouTubeSearchResult? match = YouTubeMatchHelper.PickBestMatch(
            results,
            MakeSong("Tiny Interlude", "Nobody Famous", 30));

        Assert.Equal("first", match?.VideoId);
    }

    [Fact]
    public void EmptyResults_ReturnsNull()
    {
        Assert.Null(YouTubeMatchHelper.PickBestMatch(
            Array.Empty<YouTubeSearchResult>(),
            MakeSong("Anything", "Anyone", 100)));
    }
}
