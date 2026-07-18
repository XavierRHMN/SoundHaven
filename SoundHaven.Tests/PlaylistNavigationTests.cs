using SoundHaven.Models;
using SoundHaven.ViewModels;

namespace SoundHaven.Tests;

public sealed class PlaylistNavigationTests
{
    [Fact]
    public void GetPreviousNextSong_NextWrapsToStart()
    {
        var first = new Song { Title = "First" };
        var second = new Song { Title = "Second" };
        var playlist = new Playlist { Name = "Queue" };
        playlist.Songs.Add(first);
        playlist.Songs.Add(second);

        // Next moves forward and wraps around at the end.
        Assert.Same(
            second,
            playlist.GetPreviousNextSong(first, PlaybackViewModel.Direction.Next));
        Assert.Same(
            first,
            playlist.GetPreviousNextSong(second, PlaybackViewModel.Direction.Next));
    }

    [Fact]
    public void GetPreviousNextSong_PreviousDoesNotWrap()
    {
        var first = new Song { Title = "First" };
        var second = new Song { Title = "Second" };
        var playlist = new Playlist { Name = "Queue" };
        playlist.Songs.Add(first);
        playlist.Songs.Add(second);

        // Previous moves back, but the first track returns null so the caller
        // restarts the current song instead of wrapping to the end.
        Assert.Same(
            first,
            playlist.GetPreviousNextSong(second, PlaybackViewModel.Direction.Previous));
        Assert.Null(
            playlist.GetPreviousNextSong(first, PlaybackViewModel.Direction.Previous));
    }

    [Fact]
    public void GetPreviousNextSong_ReturnsNullForUnknownCurrentSong()
    {
        var playlist = new Playlist { Name = "Queue" };
        playlist.Songs.Add(new Song { Title = "Known" });

        Song? result = playlist.GetPreviousNextSong(
            new Song { Title = "Unknown" },
            PlaybackViewModel.Direction.Next);

        Assert.Null(result);
    }
}
