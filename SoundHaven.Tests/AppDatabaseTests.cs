using Microsoft.Data.Sqlite;
using SoundHaven.Data;
using SoundHaven.Models;
using SoundHaven.Stores;

namespace SoundHaven.Tests;

public sealed class AppDatabaseTests : IDisposable
{
    private readonly string _directory;
    private readonly string _databasePath;
    private readonly AppDatabase _database;

    public AppDatabaseTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "SoundHaven.Tests",
            Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(_directory, "test.db");
        _database = new AppDatabase(_databasePath);
    }

    [Fact]
    public void SavePlaylist_RoundTripsSongsAndAssignsIdentifiers()
    {
        var song = new Song
        {
            Title = "Test track",
            Artist = "Test artist",
            Album = "Test album",
            Duration = TimeSpan.FromSeconds(123),
            FilePath = @"C:\Music\Test track.m4a",
            Year = 2026
        };
        var playlist = new Playlist { Name = "Test playlist" };
        playlist.Songs.Add(song);

        _database.SavePlaylist(playlist);
        Playlist loaded = Assert.Single(_database.GetAllPlaylists());
        Song loadedSong = Assert.Single(loaded.Songs);

        Assert.True(playlist.Id > 0);
        Assert.True(song.Id > 0);
        Assert.Equal(playlist.Id, loaded.Id);
        Assert.Equal("Test playlist", loaded.Name);
        Assert.Equal(string.Empty, loaded.Description);
        Assert.Empty(loaded.CoverImageData);
        Assert.Equal("Test track", loadedSong.Title);
        Assert.Equal(song.Duration, loadedSong.Duration);
        Assert.Equal(song.FilePath, loadedSong.FilePath);
    }

    [Fact]
    public void DislikedSongs_RoundTripAndFilterMatching()
    {
        _database.AddDislikedSong("vid00000001", "Bad Song", "Bad Artist");
        _database.AddDislikedSong(null, "Local Misfire", "Some Band");

        var store = new DislikedSongsStore(_database);

        // videoId match wins even when the metadata differs.
        Assert.True(store.IsDisliked("vid00000001", "Retitled", "Whoever"));
        // title+artist match works without a videoId (case-insensitive).
        Assert.True(store.IsDisliked(null, "local misfire", "some band"));
        Assert.False(store.IsDisliked("other000001", "Good Song", "Good Artist"));

        // A fresh store instance reloads the persisted list.
        var reloaded = new DislikedSongsStore(new AppDatabase(_databasePath));
        Assert.True(reloaded.IsDisliked(new Song { Title = "Bad Song", Artist = "Bad Artist" }));
    }

    [Fact]
    public void UpdatePlaylistDetails_PersistsDescriptionAndCover()
    {
        var playlist = new Playlist { Name = "Cover playlist" };
        _database.SavePlaylist(playlist);

        byte[] cover = [1, 2, 3, 4, 5];
        _database.UpdatePlaylistDetails(playlist.Id, "Renamed", "A short description", cover);

        Playlist loaded = Assert.Single(_database.GetAllPlaylists());
        Assert.Equal("Renamed", loaded.Name);
        Assert.Equal("A short description", loaded.Description);
        Assert.Equal(cover, loaded.CoverImageData);
    }

    [Fact]
    public void RemovePlaylist_RemovesPlaylistAndLinks()
    {
        var playlist = new Playlist { Name = "Disposable playlist" };
        playlist.Songs.Add(new Song
        {
            Title = "Disposable track",
            FilePath = @"C:\Music\Disposable track.mp3"
        });
        _database.SavePlaylist(playlist);

        _database.RemovePlaylist(playlist);

        Assert.Empty(_database.GetAllPlaylists());
    }

    [Fact]
    public void InitializeDatabase_SetsSchemaVersionAndForeignKeys()
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                ForeignKeys = true
            }.ToString());
        connection.Open();

        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        long version = (long)(versionCommand.ExecuteScalar() ?? 0L);

        using var foreignKeysCommand = connection.CreateCommand();
        foreignKeysCommand.CommandText = "PRAGMA foreign_keys;";
        long foreignKeys = (long)(foreignKeysCommand.ExecuteScalar() ?? 0L);

        Assert.Equal(5, version);
        Assert.Equal(1, foreignKeys);
    }

    [Fact]
    public void InitializeDatabase_MigratesVersionZeroLinksAndAddsCascades()
    {
        string legacyPath = Path.Combine(_directory, "legacy-schema.db");
        using (var connection = new SqliteConnection(
                   new SqliteConnectionStringBuilder { DataSource = legacyPath }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE Songs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    Artist TEXT,
                    Album TEXT,
                    Duration REAL,
                    FilePath TEXT,
                    Genre TEXT,
                    Year INTEGER,
                    PlayCount INTEGER DEFAULT 0,
                    VideoId TEXT,
                    ArtworkData BLOB,
                    ChannelTitle TEXT,
                    Views TEXT,
                    VideoDuration TEXT
                );
                CREATE TABLE Playlists (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                );
                CREATE TABLE PlaylistSongs (
                    PlaylistId INTEGER NOT NULL,
                    SongId INTEGER NOT NULL
                );
                INSERT INTO Songs (Id, Title) VALUES (1, 'Migrated track');
                INSERT INTO Playlists (Id, Name) VALUES (1, 'Migrated playlist');
                INSERT INTO PlaylistSongs (PlaylistId, SongId) VALUES (1, 1);
                INSERT INTO PlaylistSongs (PlaylistId, SongId) VALUES (1, 999);";
            command.ExecuteNonQuery();
        }

        var migratedDatabase = new AppDatabase(legacyPath);
        Playlist migratedPlaylist = Assert.Single(migratedDatabase.GetAllPlaylists());
        Assert.Equal("Migrated track", Assert.Single(migratedPlaylist.Songs).Title);

        using var migratedConnection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = legacyPath,
                ForeignKeys = true
            }.ToString());
        migratedConnection.Open();

        using (var foreignKeyCommand = migratedConnection.CreateCommand())
        {
            foreignKeyCommand.CommandText = "PRAGMA foreign_key_list('PlaylistSongs');";
            using SqliteDataReader reader = foreignKeyCommand.ExecuteReader();
            int foreignKeyCount = 0;
            while (reader.Read())
            {
                foreignKeyCount++;
                Assert.Equal("CASCADE", reader.GetString(6));
            }

            Assert.Equal(2, foreignKeyCount);
        }

        migratedDatabase.RemovePlaylist(migratedPlaylist);

        using var linkCountCommand = migratedConnection.CreateCommand();
        linkCountCommand.CommandText = "SELECT COUNT(*) FROM PlaylistSongs;";
        long remainingLinks = (long)(linkCountCommand.ExecuteScalar() ?? -1L);
        Assert.Equal(0, remainingLinks);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
