using SoundHaven.Models;
using SoundHaven.Services;
using SoundHaven.ViewModels;

namespace SoundHaven.Tests;

public sealed class PlaybackSourceTests
{
    [Fact]
    public void SelectPlaybackSource_PrefersExistingDownloadedFile()
    {
        var song = new Song
        {
            FilePath = @"C:\Music\track.m4a",
            VideoId = "jNQXAC9IVRw"
        };

        PlaybackSource source = PlaybackViewModel.SelectPlaybackSource(song, _ => true);

        var local = Assert.IsType<PlaybackSource.LocalFile>(source);
        Assert.Equal(song.FilePath, local.FilePath);
        Assert.False(song.IsYouTubeVideo);
    }

    [Fact]
    public void SelectPlaybackSource_FallsBackToYouTubeWhenDownloadIsMissing()
    {
        var song = new Song
        {
            FilePath = @"C:\Music\missing.m4a",
            VideoId = "jNQXAC9IVRw"
        };

        PlaybackSource source = PlaybackViewModel.SelectPlaybackSource(song, _ => false);

        var youTube = Assert.IsType<PlaybackSource.YouTube>(source);
        Assert.Equal(song.VideoId, youTube.VideoId);
    }

    [Fact]
    public void SelectPlaybackSource_KeepsMissingLocalPathForActionableError()
    {
        var song = new Song { FilePath = @"C:\Music\missing.mp3" };

        PlaybackSource source = PlaybackViewModel.SelectPlaybackSource(song, _ => false);

        Assert.IsType<PlaybackSource.LocalFile>(source);
    }

    [Fact]
    public void SelectPlaybackSource_RejectsSongWithoutSource()
    {
        var song = new Song();

        Assert.Throws<InvalidOperationException>(
            () => PlaybackViewModel.SelectPlaybackSource(song, _ => false));
    }
}
