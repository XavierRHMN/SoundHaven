using System.Collections.Generic;
using System.Linq;
using SoundHaven.Helpers;
using SoundHaven.Models;
using Xunit;

namespace SoundHaven.Tests;

public sealed class RecommendationFeedTests
{
    [Fact]
    public void MergeInterleaved_AlternatesSourcesAndCapsDisplay()
    {
        var ytm = Enumerable.Range(1, 15)
            .Select(i => new Song { Title = $"Y{i}", Artist = "A" })
            .ToList();
        var lastFm = Enumerable.Range(1, 15)
            .Select(i => new Song { Title = $"L{i}", Artist = "B" })
            .ToList();

        IReadOnlyList<Song> merged = RecommendationFeed.MergeInterleaved(ytm, lastFm);

        Assert.Equal(RecommendationFeed.MaxDisplay, merged.Count);
        Assert.Equal("Y1", merged[0].Title);
        Assert.Equal("L1", merged[1].Title);
        Assert.Equal("Y2", merged[2].Title);
        Assert.Equal("L2", merged[3].Title);
    }

    [Fact]
    public void MergeInterleaved_DropsDuplicateTitleArtist()
    {
        var ytm = new List<Song>
        {
            new() { Title = "Same Song", Artist = "Same Artist", VideoId = "abc" },
            new() { Title = "Other", Artist = "X", VideoId = "def" }
        };
        var lastFm = new List<Song>
        {
            new() { Title = "same song", Artist = "same artist" },
            new() { Title = "Last Only", Artist = "Y" }
        };

        IReadOnlyList<Song> merged = RecommendationFeed.MergeInterleaved(ytm, lastFm);

        Assert.Equal(3, merged.Count);
        Assert.Contains(merged, song => song.Title == "Same Song");
        Assert.Contains(merged, song => song.Title == "Other");
        Assert.Contains(merged, song => song.Title == "Last Only");
        Assert.DoesNotContain(merged, song => song.Title == "same song");
    }

    [Fact]
    public void MergeInterleaved_WorksWhenOneSourceEmpty()
    {
        var ytm = new List<Song>
        {
            new() { Title = "Only YTM", Artist = "A", VideoId = "vid" }
        };

        IReadOnlyList<Song> merged = RecommendationFeed.MergeInterleaved(ytm, []);

        Assert.Single(merged);
        Assert.Equal("Only YTM", merged[0].Title);
    }

    [Fact]
    public void MergeInterleaved_SkipsBlankTitles()
    {
        var ytm = new List<Song>
        {
            new() { Title = " ", Artist = "A", VideoId = "vid" },
            new() { Title = "Good", Artist = "A", VideoId = "vid2" }
        };

        IReadOnlyList<Song> merged = RecommendationFeed.MergeInterleaved(ytm, []);

        Assert.Single(merged);
        Assert.Equal("Good", merged[0].Title);
    }
}
