using SoundHaven.Data;
using SoundHaven.Models;
using SoundHaven.Stores;

namespace SoundHaven.Tests;

public sealed class LikedSongsStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _databasePath;

    public LikedSongsStoreTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "SoundHaven.Tests",
            Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(_directory, "liked.db");
    }

    [Fact]
    public void Store_CreatesLikedSongsPlaylistPinnedFirst()
    {
        var store = new PlaylistStore(new AppDatabase(_databasePath));

        Assert.NotNull(store.LikedSongsPlaylist);
        Assert.True(store.LikedSongsPlaylist.IsLikedSongs);
        Assert.Equal("Liked Songs", store.LikedSongsPlaylist.Name);
        Assert.True(store.LikedSongsPlaylist.Id > 0);
        Assert.Same(store.LikedSongsPlaylist, store.Playlists[0]);
    }

    [Fact]
    public void ToggleFavorite_AddsThenRemoves()
    {
        var store = new PlaylistStore(new AppDatabase(_databasePath));
        var song = new Song { Title = "Glare", Artist = "Blank", VideoId = "vid00000001" };

        Assert.False(store.IsFavorite(song));

        Assert.True(store.ToggleFavorite(song));
        Assert.True(store.IsFavorite(song));
        Assert.Single(store.LikedSongsPlaylist.Songs);

        Assert.False(store.ToggleFavorite(song));
        Assert.False(store.IsFavorite(song));
        Assert.Empty(store.LikedSongsPlaylist.Songs);
    }

    [Fact]
    public void IsFavorite_MatchesByVideoIdAcrossInstances()
    {
        var store = new PlaylistStore(new AppDatabase(_databasePath));
        store.ToggleFavorite(new Song { Title = "Glare", Artist = "Blank", VideoId = "vid00000001" });

        // A different Song object carrying the same VideoId is still favorited.
        var other = new Song { Title = "Glare (remaster)", Artist = "Blank", VideoId = "vid00000001" };
        Assert.True(store.IsFavorite(other));
    }

    [Fact]
    public void RemovePlaylist_IgnoresLikedSongsPlaylist()
    {
        var store = new PlaylistStore(new AppDatabase(_databasePath));
        Playlist liked = store.LikedSongsPlaylist;

        store.RemovePlaylist(liked);

        Assert.Contains(liked, store.Playlists);
        Assert.True(liked.Id > 0);
    }

    [Fact]
    public void Store_ReusesExistingLikedSongsPlaylistAndKeepsLikes()
    {
        var first = new PlaylistStore(new AppDatabase(_databasePath));
        first.ToggleFavorite(new Song { Title = "Glare", Artist = "Blank", VideoId = "vid00000001" });
        int likedId = first.LikedSongsPlaylist.Id;

        // A fresh store over the same database must not create a duplicate.
        var reloaded = new PlaylistStore(new AppDatabase(_databasePath));
        Assert.Single(reloaded.Playlists, playlist => playlist.IsLikedSongs);
        Assert.Equal(likedId, reloaded.LikedSongsPlaylist.Id);
        Assert.True(reloaded.IsFavorite(new Song { VideoId = "vid00000001", Title = "Glare" }));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
