using SoundHaven.Models;
using SoundHaven.ViewModels;

namespace SoundHaven.Tests;

public sealed class PlaylistNavigationTests
{
    [Fact]
    public void GetPreviousNextSong_WrapsInBothDirections()
    {
        var first = new Song { Title = "First" };
        var second = new Song { Title = "Second" };
        var playlist = new Playlist { Name = "Queue" };
        playlist.Songs.Add(first);
        playlist.Songs.Add(second);

        Assert.Same(
            first,
            playlist.GetPreviousNextSong(second, PlaybackViewModel.Direction.Next));
        Assert.Same(
            second,
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
