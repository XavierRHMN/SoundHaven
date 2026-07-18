using SoundHaven.Data;
using SoundHaven.Models;
using SoundHaven.Stores;

namespace SoundHaven.Tests;

public sealed class DownloadedSongsStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _databasePath;

    public DownloadedSongsStoreTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "SoundHaven.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "downloads.db");
    }

    private string CreateAudioFile(string name)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        return path;
    }

    [Fact]
    public void Store_CreatesDownloadedSongsPlaylistPinnedSecond()
    {
        var store = new PlaylistStore(new AppDatabase(_databasePath));

        Assert.NotNull(store.DownloadedSongsPlaylist);
        Assert.True(store.DownloadedSongsPlaylist.IsDownloads);
        Assert.Equal("Downloaded Songs", store.DownloadedSongsPlaylist.Name);
        Assert.True(store.DownloadedSongsPlaylist.Id > 0);
        Assert.Same(store.LikedSongsPlaylist, store.Playlists[0]);
        Assert.Same(store.DownloadedSongsPlaylist, store.Playlists[1]);
    }

    [Fact]
    public void Store_ReusesExistingDownloadedSongsPlaylist()
    {
        var first = new PlaylistStore(new AppDatabase(_databasePath));
        int downloadsId = first.DownloadedSongsPlaylist.Id;

        var reloaded = new PlaylistStore(new AppDatabase(_databasePath));
        Assert.Single(reloaded.Playlists, playlist => playlist.IsDownloads);
        Assert.Equal(downloadsId, reloaded.DownloadedSongsPlaylist.Id);
    }

    [Fact]
    public void MarkDownloaded_AddsThenMarkUndownloadedRemoves()
    {
        var store = new PlaylistStore(new AppDatabase(_databasePath));
        var song = new Song
        {
            Title = "Glare",
            Artist = "Blank",
            VideoId = "vid00000001",
            FilePath = CreateAudioFile("glare.m4a")
        };

        store.MarkDownloaded(song);
        Assert.Single(store.DownloadedSongsPlaylist.Songs);
        Assert.True(song.Id > 0);

        store.MarkUndownloaded(song);
        Assert.Empty(store.DownloadedSongsPlaylist.Songs);
    }

    [Fact]
    public void MarkDownloaded_IsIdempotentAcrossInstances()
    {
        var store = new PlaylistStore(new AppDatabase(_databasePath));
        string filePath = CreateAudioFile("glare.m4a");

        store.MarkDownloaded(new Song { Title = "Glare", VideoId = "vid00000001", FilePath = filePath });
        store.MarkDownloaded(new Song { Title = "Glare", VideoId = "vid00000001", FilePath = filePath });

        Assert.Single(store.DownloadedSongsPlaylist.Songs);
    }

    [Fact]
    public void Reconcile_BackfillsOfflineSongsFromOtherPlaylists()
    {
        var first = new PlaylistStore(new AppDatabase(_databasePath));
        var playlist = new Playlist { Name = "Mix" };
        first.AddPlaylist(playlist);
        first.AddSongToPlaylist(playlist, new Song
        {
            Title = "Glare",
            VideoId = "vid00000001",
            FilePath = CreateAudioFile("glare.m4a")
        });

        // A fresh store derives Downloaded Songs membership from what's on disk.
        var reloaded = new PlaylistStore(new AppDatabase(_databasePath));
        Song member = Assert.Single(reloaded.DownloadedSongsPlaylist.Songs);
        Assert.Equal("Glare", member.Title);
        Assert.Equal(DownloadState.Downloaded, member.CurrentDownloadState);
    }

    [Fact]
    public void Reconcile_PrunesMembersWhoseFileVanished()
    {
        string filePath = CreateAudioFile("glare.m4a");
        var first = new PlaylistStore(new AppDatabase(_databasePath));
        first.MarkDownloaded(new Song { Title = "Glare", VideoId = "vid00000001", FilePath = filePath });
        Assert.Single(first.DownloadedSongsPlaylist.Songs);

        File.Delete(filePath);

        var reloaded = new PlaylistStore(new AppDatabase(_databasePath));
        Assert.Empty(reloaded.DownloadedSongsPlaylist.Songs);
    }

    [Fact]
    public void Reconcile_IgnoresLocalFilesWithoutVideoId()
    {
        var first = new PlaylistStore(new AppDatabase(_databasePath));
        var playlist = new Playlist { Name = "Local" };
        first.AddPlaylist(playlist);
        first.AddSongToPlaylist(playlist, new Song
        {
            Title = "Imported",
            FilePath = CreateAudioFile("imported.m4a")
        });

        // User-imported files play offline but aren't downloads.
        var reloaded = new PlaylistStore(new AppDatabase(_databasePath));
        Assert.Empty(reloaded.DownloadedSongsPlaylist.Songs);
    }

    [Fact]
    public void RemovePlaylist_IgnoresDownloadedSongsPlaylist()
    {
        var store = new PlaylistStore(new AppDatabase(_databasePath));
        Playlist downloads = store.DownloadedSongsPlaylist;

        store.RemovePlaylist(downloads);

        Assert.Contains(downloads, store.Playlists);
        Assert.True(downloads.Id > 0);
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
