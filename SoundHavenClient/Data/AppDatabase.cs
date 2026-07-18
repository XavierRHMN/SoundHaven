using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using SoundHaven.Models;

namespace SoundHaven.Data
{
    public class AppDatabase
    {
        private const int CurrentSchemaVersion = 4;
        private const string DatabaseDirectoryName = "SoundHaven";
        private const string DatabaseFileName = "AppDatabase.db";
        private const string LegacyDatabaseFileName = "AppdataBase.db";

        private readonly string _connectionString;

        public AppDatabase(string? dbPath = null)
        {
            string databasePath = ResolveDatabasePath(dbPath);
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                ForeignKeys = true
            }.ToString();

            InitializeDatabase();
        }

        private static string ResolveDatabasePath(string? dbPath)
        {
            if (!string.IsNullOrWhiteSpace(dbPath))
            {
                string customPath = Path.GetFullPath(dbPath);
                string? customDirectory = Path.GetDirectoryName(customPath);
                if (!string.IsNullOrEmpty(customDirectory))
                {
                    Directory.CreateDirectory(customDirectory);
                }

                return customPath;
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                throw new InvalidOperationException("The local application data directory is unavailable.");
            }

            string databaseDirectory = Path.Combine(localAppData, DatabaseDirectoryName);
            Directory.CreateDirectory(databaseDirectory);

            string databasePath = Path.Combine(databaseDirectory, DatabaseFileName);
            MigrateLegacyDatabase(databasePath);
            return databasePath;
        }

        private static void MigrateLegacyDatabase(string databasePath)
        {
            if (File.Exists(databasePath))
            {
                return;
            }

            string legacyPath = Path.Combine(AppContext.BaseDirectory, LegacyDatabaseFileName);
            if (!File.Exists(legacyPath) || PathsAreEqual(legacyPath, databasePath))
            {
                return;
            }

            string temporaryPath = $"{databasePath}.{Guid.NewGuid():N}.migrating";
            try
            {
                using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = legacyPath,
                    Mode = SqliteOpenMode.ReadOnly
                }.ToString()))
                using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = temporaryPath,
                    Mode = SqliteOpenMode.ReadWriteCreate
                }.ToString()))
                {
                    source.Open();
                    destination.Open();
                    source.BackupDatabase(destination);

                    using var checkCommand = destination.CreateCommand();
                    checkCommand.CommandText = "PRAGMA quick_check;";
                    string? result = checkCommand.ExecuteScalar()?.ToString();
                    if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"The legacy SoundHaven database failed validation: {result ?? "no result"}.");
                    }
                }

                try
                {
                    File.Move(temporaryPath, databasePath);
                }
                catch (IOException) when (File.Exists(databasePath))
                {
                    // Another SoundHaven process completed the one-time migration first.
                }
            }
            finally
            {
                TryDeleteFile(temporaryPath);
                TryDeleteFile($"{temporaryPath}-journal");
                TryDeleteFile($"{temporaryPath}-shm");
                TryDeleteFile($"{temporaryPath}-wal");
            }
        }

        private static bool PathsAreEqual(string firstPath, string secondPath)
        {
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return string.Equals(
                Path.GetFullPath(firstPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(secondPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                comparison);
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void InitializeDatabase()
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            int schemaVersion = GetSchemaVersion(connection, transaction);
            if (schemaVersion > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Database schema version {schemaVersion} is newer than supported version {CurrentSchemaVersion}.");
            }

            CreateCoreTables(connection, transaction);

            bool playlistSongsExists = TableExists(connection, transaction, "PlaylistSongs");
            if (!playlistSongsExists)
            {
                CreatePlaylistSongsTable(connection, transaction);
            }
            else if (schemaVersion < 1)
            {
                RebuildPlaylistSongsTable(connection, transaction);
            }

            if (schemaVersion < 2)
            {
                MigratePlaylistsToV2(connection, transaction);
            }

            if (schemaVersion < 3)
            {
                MigratePlaylistsToV3(connection, transaction);
            }

            if (schemaVersion < 4)
            {
                MigratePlaylistsToV4(connection, transaction);
            }

            using (var indexCommand = connection.CreateCommand())
            {
                indexCommand.Transaction = transaction;
                indexCommand.CommandText =
                    "CREATE INDEX IF NOT EXISTS IX_PlaylistSongs_SongId ON PlaylistSongs (SongId);";
                indexCommand.ExecuteNonQuery();
            }

            using (var settingsCommand = connection.CreateCommand())
            {
                settingsCommand.Transaction = transaction;
                settingsCommand.CommandText = @"
                    INSERT INTO AppSettings ([Key], Value)
                    VALUES ('SchemaVersion', @version)
                    ON CONFLICT([Key]) DO UPDATE SET Value = excluded.Value;";
                settingsCommand.Parameters.AddWithValue(
                    "@version",
                    CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture));
                settingsCommand.ExecuteNonQuery();
            }

            using (var versionCommand = connection.CreateCommand())
            {
                versionCommand.Transaction = transaction;
                versionCommand.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion};";
                versionCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        private static int GetSchemaVersion(SqliteConnection connection, SqliteTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static void CreateCoreTables(SqliteConnection connection, SqliteTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Songs (
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

                CREATE TABLE IF NOT EXISTS Playlists (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Description TEXT NOT NULL DEFAULT '',
                    CoverImageData BLOB,
                    CreatedAt TEXT,
                    UpdatedAt TEXT,
                    IsLikedSongs INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS AppSettings (
                    [Key] TEXT PRIMARY KEY,
                    Value TEXT
                );

                CREATE TABLE IF NOT EXISTS ThemeSettings (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ColorHex TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS DislikedSongs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    VideoId TEXT,
                    Title TEXT NOT NULL,
                    Artist TEXT,
                    CreatedAt TEXT
                );";
            command.ExecuteNonQuery();
        }

        public void AddDislikedSong(string? videoId, string title, string? artist)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(title);

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO DislikedSongs (VideoId, Title, Artist, CreatedAt)
                VALUES (@videoId, @title, @artist, @createdAt);";
            command.Parameters.AddWithValue("@videoId", (object?)videoId ?? DBNull.Value);
            command.Parameters.AddWithValue("@title", title);
            command.Parameters.AddWithValue("@artist", (object?)artist ?? DBNull.Value);
            command.Parameters.AddWithValue("@createdAt", UtcNowText());
            command.ExecuteNonQuery();
        }

        public List<DislikedSong> GetDislikedSongs()
        {
            var results = new List<DislikedSong>();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT VideoId, Title, Artist FROM DislikedSongs;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new DislikedSong(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }

            return results;
        }

        private static void MigratePlaylistsToV2(
            SqliteConnection connection,
            SqliteTransaction transaction)
        {
            EnsureColumnExists(
                connection,
                transaction,
                "Playlists",
                "Description",
                "ALTER TABLE Playlists ADD COLUMN Description TEXT NOT NULL DEFAULT '';");
            EnsureColumnExists(
                connection,
                transaction,
                "Playlists",
                "CoverImageData",
                "ALTER TABLE Playlists ADD COLUMN CoverImageData BLOB;");
        }

        private static void MigratePlaylistsToV3(
            SqliteConnection connection,
            SqliteTransaction transaction)
        {
            EnsureColumnExists(
                connection,
                transaction,
                "Playlists",
                "CreatedAt",
                "ALTER TABLE Playlists ADD COLUMN CreatedAt TEXT;");
            EnsureColumnExists(
                connection,
                transaction,
                "Playlists",
                "UpdatedAt",
                "ALTER TABLE Playlists ADD COLUMN UpdatedAt TEXT;");
        }

        private static void MigratePlaylistsToV4(
            SqliteConnection connection,
            SqliteTransaction transaction)
        {
            EnsureColumnExists(
                connection,
                transaction,
                "Playlists",
                "IsLikedSongs",
                "ALTER TABLE Playlists ADD COLUMN IsLikedSongs INTEGER NOT NULL DEFAULT 0;");
        }

        private static string UtcNowText() =>
            DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        private static DateTime? ParseTimestamp(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out DateTime parsed)
                ? parsed
                : null;
        }

        private static void TouchPlaylist(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            long playlistId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE Playlists SET UpdatedAt = @now WHERE Id = @id;";
            command.Parameters.AddWithValue("@now", UtcNowText());
            command.Parameters.AddWithValue("@id", playlistId);
            command.ExecuteNonQuery();
        }

        private static void EnsureColumnExists(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string tableName,
            string columnName,
            string alterSql)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"PRAGMA table_info({tableName});";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            reader.Close();

            using var alterCommand = connection.CreateCommand();
            alterCommand.Transaction = transaction;
            alterCommand.CommandText = alterSql;
            alterCommand.ExecuteNonQuery();
        }

        private static bool TableExists(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string tableName)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                SELECT EXISTS (
                    SELECT 1
                    FROM sqlite_master
                    WHERE type = 'table' AND name = @tableName
                );";
            command.Parameters.AddWithValue("@tableName", tableName);
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
        }

        private static void CreatePlaylistSongsTable(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string tableName = "PlaylistSongs")
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
                CREATE TABLE {tableName} (
                    PlaylistId INTEGER NOT NULL,
                    SongId INTEGER NOT NULL,
                    PRIMARY KEY (PlaylistId, SongId),
                    FOREIGN KEY (PlaylistId) REFERENCES Playlists(Id) ON DELETE CASCADE,
                    FOREIGN KEY (SongId) REFERENCES Songs(Id) ON DELETE CASCADE
                );";
            command.ExecuteNonQuery();
        }

        private static void RebuildPlaylistSongsTable(
            SqliteConnection connection,
            SqliteTransaction transaction)
        {
            const string migrationTable = "PlaylistSongs_v1";

            using (var dropCommand = connection.CreateCommand())
            {
                dropCommand.Transaction = transaction;
                dropCommand.CommandText = $"DROP TABLE IF EXISTS {migrationTable};";
                dropCommand.ExecuteNonQuery();
            }

            CreatePlaylistSongsTable(connection, transaction, migrationTable);

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
                INSERT OR IGNORE INTO {migrationTable} (PlaylistId, SongId)
                SELECT ps.PlaylistId, ps.SongId
                FROM PlaylistSongs ps
                INNER JOIN Playlists p ON p.Id = ps.PlaylistId
                INNER JOIN Songs s ON s.Id = ps.SongId;

                DROP TABLE PlaylistSongs;
                ALTER TABLE {migrationTable} RENAME TO PlaylistSongs;";
            command.ExecuteNonQuery();
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            try
            {
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA foreign_keys = ON;";
                command.ExecuteNonQuery();

                command.CommandText = "PRAGMA foreign_keys;";
                if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
                {
                    throw new InvalidOperationException("SQLite foreign key enforcement could not be enabled.");
                }

                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        public void SaveThemeColor(string colorHex)
        {
            ArgumentNullException.ThrowIfNull(colorHex);

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO ThemeSettings (Id, ColorHex)
                VALUES (1, @colorHex);";
            command.Parameters.AddWithValue("@colorHex", colorHex);
            command.ExecuteNonQuery();
        }

        public string GetThemeColor()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT ColorHex FROM ThemeSettings WHERE Id = 1";
            return command.ExecuteScalar()?.ToString() ?? string.Empty;
        }

        public void UpdatePlaylistName(int playlistId, string newName)
        {
            UpdatePlaylistDetails(playlistId, newName, description: null, coverImageData: null, updateDescription: false, updateCover: false);
        }

        /// <summary>Persists (or clears, when <paramref name="filePath"/> is null) a
        /// downloaded song's local file path so its offline status survives restarts.</summary>
        public void UpdateSongFilePath(long songId, string? filePath)
        {
            if (songId <= 0)
            {
                return;
            }

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Songs SET FilePath = @filePath WHERE Id = @id;";
            command.Parameters.AddWithValue(
                "@filePath",
                string.IsNullOrWhiteSpace(filePath) ? DBNull.Value : filePath);
            command.Parameters.AddWithValue("@id", songId);
            command.ExecuteNonQuery();
        }

        public void UpdatePlaylistDetails(
            int playlistId,
            string name,
            string description,
            byte[]? coverImageData)
        {
            UpdatePlaylistDetails(
                playlistId,
                name,
                description,
                coverImageData,
                updateDescription: true,
                updateCover: true);
        }

        private void UpdatePlaylistDetails(
            int playlistId,
            string name,
            string? description,
            byte[]? coverImageData,
            bool updateDescription,
            bool updateCover)
        {
            if (playlistId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(playlistId), "A saved playlist ID is required.");
            }

            ArgumentNullException.ThrowIfNull(name);

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            if (updateDescription && updateCover)
            {
                command.CommandText = @"
                    UPDATE Playlists
                    SET Name = @name,
                        Description = @description,
                        CoverImageData = @cover,
                        UpdatedAt = @now
                    WHERE Id = @id;";
                command.Parameters.AddWithValue("@description", description ?? string.Empty);
                command.Parameters.AddWithValue(
                    "@cover",
                    (object?)coverImageData ?? DBNull.Value);
            }
            else
            {
                command.CommandText =
                    "UPDATE Playlists SET Name = @name, UpdatedAt = @now WHERE Id = @id;";
            }

            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@now", UtcNowText());
            command.Parameters.AddWithValue("@id", playlistId);
            command.ExecuteNonQuery();
        }

        public void SavePlaylist(Playlist playlist)
        {
            ArgumentNullException.ThrowIfNull(playlist);
            ArgumentNullException.ThrowIfNull(playlist.Songs);

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            long playlistId = playlist.Id;
            if (playlistId > 0)
            {
                using var updateCommand = connection.CreateCommand();
                updateCommand.Transaction = transaction;
                updateCommand.CommandText = @"
                    UPDATE Playlists
                    SET Name = @name,
                        Description = @description,
                        CoverImageData = @cover,
                        IsLikedSongs = @isLiked,
                        UpdatedAt = @now
                    WHERE Id = @id;";
                updateCommand.Parameters.AddWithValue("@name", playlist.Name);
                updateCommand.Parameters.AddWithValue("@description", playlist.Description ?? string.Empty);
                updateCommand.Parameters.AddWithValue(
                    "@cover",
                    playlist.CoverImageData is { Length: > 0 }
                        ? playlist.CoverImageData
                        : DBNull.Value);
                updateCommand.Parameters.AddWithValue("@isLiked", playlist.IsLikedSongs ? 1 : 0);
                updateCommand.Parameters.AddWithValue("@now", UtcNowText());
                updateCommand.Parameters.AddWithValue("@id", playlistId);
                if (updateCommand.ExecuteNonQuery() == 0)
                {
                    throw new InvalidOperationException($"Playlist {playlistId} does not exist.");
                }
            }
            else
            {
                using var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = @"
                    INSERT INTO Playlists (Name, Description, CoverImageData, CreatedAt, UpdatedAt, IsLikedSongs)
                    VALUES (@name, @description, @cover, @now, @now, @isLiked);";
                insertCommand.Parameters.AddWithValue("@name", playlist.Name);
                insertCommand.Parameters.AddWithValue("@description", playlist.Description ?? string.Empty);
                insertCommand.Parameters.AddWithValue(
                    "@cover",
                    playlist.CoverImageData is { Length: > 0 }
                        ? playlist.CoverImageData
                        : DBNull.Value);
                insertCommand.Parameters.AddWithValue("@isLiked", playlist.IsLikedSongs ? 1 : 0);
                insertCommand.Parameters.AddWithValue("@now", UtcNowText());
                insertCommand.ExecuteNonQuery();

                insertCommand.CommandText = "SELECT last_insert_rowid();";
                insertCommand.Parameters.Clear();
                playlistId = Convert.ToInt64(insertCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
            }

            using (var clearCommand = connection.CreateCommand())
            {
                clearCommand.Transaction = transaction;
                clearCommand.CommandText = "DELETE FROM PlaylistSongs WHERE PlaylistId = @playlistId;";
                clearCommand.Parameters.AddWithValue("@playlistId", playlistId);
                clearCommand.ExecuteNonQuery();
            }

            var savedSongs = new List<(Song Song, long Id, string Title)>();
            foreach (Song song in playlist.Songs.Distinct())
            {
                string title = GetRequiredSongTitle(song);
                long songId = GetOrCreateSong(connection, transaction, song, title);
                savedSongs.Add((song, songId, title));

                using var linkCommand = connection.CreateCommand();
                linkCommand.Transaction = transaction;
                linkCommand.CommandText = @"
                    INSERT OR IGNORE INTO PlaylistSongs (PlaylistId, SongId)
                    VALUES (@playlistId, @songId);";
                linkCommand.Parameters.AddWithValue("@playlistId", playlistId);
                linkCommand.Parameters.AddWithValue("@songId", songId);
                linkCommand.ExecuteNonQuery();
            }

            transaction.Commit();

            playlist.Id = checked((int)playlistId);
            foreach ((Song song, long songId, string title) in savedSongs)
            {
                song.Id = checked((int)songId);
                song.Title = title;
            }
        }

        public void RemovePlaylist(Playlist playlist)
        {
            ArgumentNullException.ThrowIfNull(playlist);
            if (playlist.Id <= 0)
            {
                return;
            }

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            using (var linksCommand = connection.CreateCommand())
            {
                linksCommand.Transaction = transaction;
                linksCommand.CommandText = "DELETE FROM PlaylistSongs WHERE PlaylistId = @id;";
                linksCommand.Parameters.AddWithValue("@id", playlist.Id);
                linksCommand.ExecuteNonQuery();
            }

            using (var playlistCommand = connection.CreateCommand())
            {
                playlistCommand.Transaction = transaction;
                playlistCommand.CommandText = "DELETE FROM Playlists WHERE Id = @id;";
                playlistCommand.Parameters.AddWithValue("@id", playlist.Id);
                playlistCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        public ObservableCollection<Playlist> GetAllPlaylists()
        {
            var playlists = new ObservableCollection<Playlist>();
            using var connection = OpenConnection();

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT Id, Name, Description, CoverImageData, CreatedAt, UpdatedAt, IsLikedSongs FROM Playlists ORDER BY Id;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    playlists.Add(new Playlist
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        CoverImageData = reader.IsDBNull(3)
                            ? []
                            : reader.GetFieldValue<byte[]>(3),
                        CreatedAtUtc = ParseTimestamp(reader.IsDBNull(4) ? null : reader.GetString(4)),
                        UpdatedAtUtc = ParseTimestamp(reader.IsDBNull(5) ? null : reader.GetString(5)),
                        IsLikedSongs = !reader.IsDBNull(6) && reader.GetInt32(6) != 0
                    });
                }
            }

            foreach (Playlist playlist in playlists)
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT s.Id, s.Title, s.Artist, s.Album, s.Duration, s.FilePath, s.Genre, s.Year,
                           s.PlayCount, s.ArtworkData, s.VideoId, s.ChannelTitle, s.Views, s.VideoDuration
                    FROM Songs s
                    INNER JOIN PlaylistSongs ps ON s.Id = ps.SongId
                    WHERE ps.PlaylistId = @playlistId
                    ORDER BY ps.rowid;";
                command.Parameters.AddWithValue("@playlistId", playlist.Id);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    playlist.Songs.Add(new Song
                    {
                        Id = reader.GetInt32(0),
                        Title = reader.GetString(1),
                        Artist = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Album = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Duration = TimeSpan.FromSeconds(reader.IsDBNull(4) ? 0 : reader.GetDouble(4)),
                        FilePath = reader.IsDBNull(5) ? null : reader.GetString(5),
                        Genre = reader.IsDBNull(6) ? null : reader.GetString(6),
                        Year = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                        PlayCount = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                        ArtworkData = reader.IsDBNull(9) ? Array.Empty<byte>() : reader.GetFieldValue<byte[]>(9),
                        VideoId = reader.IsDBNull(10) ? null : reader.GetString(10),
                        ChannelTitle = reader.IsDBNull(11) ? null : reader.GetString(11),
                        Views = reader.IsDBNull(12) ? null : reader.GetString(12),
                        VideoDuration = reader.IsDBNull(13) ? null : reader.GetString(13)
                    });
                }
            }

            return playlists;
        }

        public void AddSongToPlaylist(long playlistId, Song song)
        {
            if (playlistId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(playlistId), "A saved playlist ID is required.");
            }

            ArgumentNullException.ThrowIfNull(song);

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            if (!PlaylistExists(connection, transaction, playlistId))
            {
                throw new InvalidOperationException($"Playlist {playlistId} does not exist.");
            }

            string title = GetRequiredSongTitle(song);
            long songId = GetOrCreateSong(connection, transaction, song, title);

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT OR IGNORE INTO PlaylistSongs (PlaylistId, SongId)
                    VALUES (@playlistId, @songId);";
                command.Parameters.AddWithValue("@playlistId", playlistId);
                command.Parameters.AddWithValue("@songId", songId);
                command.ExecuteNonQuery();
            }

            TouchPlaylist(connection, transaction, playlistId);
            transaction.Commit();
            song.Id = checked((int)songId);
            song.Title = title;
        }

        public void RemoveSongFromPlaylist(long playlistId, long songId)
        {
            if (playlistId <= 0 || songId <= 0)
            {
                return;
            }

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                DELETE FROM PlaylistSongs
                WHERE PlaylistId = @playlistId AND SongId = @songId;";
            command.Parameters.AddWithValue("@playlistId", playlistId);
            command.Parameters.AddWithValue("@songId", songId);
            command.ExecuteNonQuery();
        }

        public void RemoveSongsFromPlaylist(long playlistId, IEnumerable<long> songIds)
        {
            ArgumentNullException.ThrowIfNull(songIds);
            if (playlistId <= 0)
            {
                return;
            }

            long[] distinctSongIds = songIds.Where(id => id > 0).Distinct().ToArray();
            if (distinctSongIds.Length == 0)
            {
                return;
            }

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                DELETE FROM PlaylistSongs
                WHERE PlaylistId = @playlistId AND SongId = @songId;";
            command.Parameters.AddWithValue("@playlistId", playlistId);
            var songIdParameter = command.Parameters.Add("@songId", SqliteType.Integer);

            foreach (long songId in distinctSongIds)
            {
                songIdParameter.Value = songId;
                command.ExecuteNonQuery();
            }

            TouchPlaylist(connection, transaction, playlistId);
            transaction.Commit();
        }

        public long GetPlaylistId(string playlistName)
        {
            ArgumentNullException.ThrowIfNull(playlistName);

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id
                FROM Playlists
                WHERE Name = @name
                ORDER BY Id DESC
                LIMIT 1;";
            command.Parameters.AddWithValue("@name", playlistName);

            object? result = command.ExecuteScalar();
            return result is null or DBNull
                ? -1
                : Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }

        private static bool PlaylistExists(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long playlistId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT EXISTS (SELECT 1 FROM Playlists WHERE Id = @id);";
            command.Parameters.AddWithValue("@id", playlistId);
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
        }

        private static long GetOrCreateSong(
            SqliteConnection connection,
            SqliteTransaction transaction,
            Song song,
            string title)
        {
            if (song.Id > 0 && SongExists(connection, transaction, song.Id))
            {
                return song.Id;
            }

            long? existingId = FindSongId(connection, transaction, song);
            if (existingId.HasValue)
            {
                return existingId.Value;
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT INTO Songs (
                    Title, Artist, Album, Duration, FilePath, Genre, Year, PlayCount,
                    ArtworkData, VideoId, ChannelTitle, Views, VideoDuration
                )
                VALUES (
                    @title, @artist, @album, @duration, @filePath, @genre, @year, @playCount,
                    @artworkData, @videoId, @channelTitle, @views, @videoDuration
                );";
            AddSongParameters(command, song, title);
            command.ExecuteNonQuery();

            command.CommandText = "SELECT last_insert_rowid();";
            command.Parameters.Clear();
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static bool SongExists(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long songId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT EXISTS (SELECT 1 FROM Songs WHERE Id = @id);";
            command.Parameters.AddWithValue("@id", songId);
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
        }

        private static long? FindSongId(
            SqliteConnection connection,
            SqliteTransaction transaction,
            Song song)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;

            if (!string.IsNullOrWhiteSpace(song.FilePath))
            {
                command.CommandText = @"
                    SELECT Id
                    FROM Songs
                    WHERE FilePath = @value COLLATE NOCASE
                    ORDER BY Id
                    LIMIT 1;";
                command.Parameters.AddWithValue("@value", song.FilePath);
            }
            else if (!string.IsNullOrWhiteSpace(song.VideoId))
            {
                command.CommandText = @"
                    SELECT Id
                    FROM Songs
                    WHERE VideoId = @value
                    ORDER BY Id
                    LIMIT 1;";
                command.Parameters.AddWithValue("@value", song.VideoId);
            }
            else
            {
                return null;
            }

            object? result = command.ExecuteScalar();
            return result is null or DBNull
                ? null
                : Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }

        private static string GetRequiredSongTitle(Song song)
        {
            if (!string.IsNullOrWhiteSpace(song.Title))
            {
                return song.Title;
            }

            if (!string.IsNullOrWhiteSpace(song.FilePath))
            {
                string fileName = Path.GetFileNameWithoutExtension(song.FilePath);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    return fileName;
                }
            }

            return "Unknown Title";
        }

        private static void AddSongParameters(SqliteCommand command, Song song, string title)
        {
            command.Parameters.AddWithValue("@title", title);
            command.Parameters.AddWithValue("@artist", (object?)song.Artist ?? DBNull.Value);
            command.Parameters.AddWithValue("@album", (object?)song.Album ?? DBNull.Value);
            command.Parameters.AddWithValue("@duration", song.Duration.TotalSeconds);
            command.Parameters.AddWithValue("@filePath", (object?)song.FilePath ?? DBNull.Value);
            command.Parameters.AddWithValue("@genre", (object?)song.Genre ?? DBNull.Value);
            command.Parameters.AddWithValue("@year", (object?)song.Year ?? DBNull.Value);
            command.Parameters.AddWithValue("@playCount", song.PlayCount);
            command.Parameters.AddWithValue("@artworkData", (object?)song.ArtworkData ?? DBNull.Value);
            command.Parameters.AddWithValue("@videoId", (object?)song.VideoId ?? DBNull.Value);
            command.Parameters.AddWithValue("@channelTitle", (object?)song.ChannelTitle ?? DBNull.Value);
            command.Parameters.AddWithValue("@views", (object?)song.Views ?? DBNull.Value);
            command.Parameters.AddWithValue("@videoDuration", (object?)song.VideoDuration ?? DBNull.Value);
        }
    }
}
