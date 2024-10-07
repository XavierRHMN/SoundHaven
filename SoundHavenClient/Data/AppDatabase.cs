using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Collections.ObjectModel;
using SoundHaven.Models;

namespace SoundHaven.Data
{
    public class AppDatabase
    {
        private string connectionString;

        public AppDatabase(string dbPath)
        {
            connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Songs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Artist TEXT,
                Album TEXT,
                Duration INTEGER,
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
                Name TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS PlaylistSongs (
                PlaylistId INTEGER,
                SongId INTEGER,
                FOREIGN KEY(PlaylistId) REFERENCES Playlists(Id),
                FOREIGN KEY(SongId) REFERENCES Songs(Id),
                PRIMARY KEY(PlaylistId, SongId)
            );

            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT
            );

            CREATE TABLE IF NOT EXISTS ThemeSettings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ColorHex TEXT NOT NULL
            );";
                command.ExecuteNonQuery();
            }
        }

        public void SaveThemeColor(string colorHex)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"
                INSERT OR REPLACE INTO ThemeSettings (Id, ColorHex) 
                VALUES (1, @colorHex);";
                command.Parameters.AddWithValue("@colorHex", colorHex);
                command.ExecuteNonQuery();
            }
        }

        public string GetThemeColor()
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT ColorHex FROM ThemeSettings WHERE Id = 1";
                object? result = command.ExecuteScalar();
                return result?.ToString();
            }
        }

        public void UpdatePlaylistName(int playlistId, string newName)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "UPDATE Playlists SET Name = @name WHERE Id = @id";
                command.Parameters.AddWithValue("@name", newName);
                command.Parameters.AddWithValue("@id", playlistId);
                command.ExecuteNonQuery();
            }
        }

        public void SavePlaylist(Playlist playlist)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO Playlists (Name) VALUES (@name);
                    SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("@name", playlist.Name);
                long playlistId = (long)command.ExecuteScalar();

                foreach (var song in playlist.Songs)
                {
                    command.CommandText = "INSERT INTO PlaylistSongs (PlaylistId, SongId) VALUES (@playlistId, @songId)";
                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@playlistId", playlistId);
                    command.Parameters.AddWithValue("@songId", song.Id);
                    command.ExecuteNonQuery();
                }
            }
        }

        public void RemovePlaylist(Playlist playlist)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"
                    DELETE FROM PlaylistSongs WHERE PlaylistId = @id;
                    DELETE FROM Playlists WHERE Id = @id;";
                command.Parameters.AddWithValue("@id", playlist.Id);
                command.ExecuteNonQuery();
            }
        }

        public ObservableCollection<Playlist> GetAllPlaylists()
        {
            var playlists = new ObservableCollection<Playlist>();
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Id, Name FROM Playlists";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var playlist = new Playlist
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1)
                        };
                        playlists.Add(playlist);
                    }
                }

                foreach (var playlist in playlists)
                {
                    command.CommandText = @"
                SELECT s.Id, s.Title, s.Artist, s.Album, s.Duration, s.FilePath, s.Genre, s.Year, s.ArtworkData,
                       s.VideoId, s.ChannelTitle, s.Views, s.VideoDuration
                FROM Songs s
                JOIN PlaylistSongs ps ON s.Id = ps.SongId
                WHERE ps.PlaylistId = @playlistId";
                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@playlistId", playlist.Id);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var song = new Song
                            {
                                Id = reader.GetInt32(0),
                                Title = reader.GetString(1),
                                Artist = reader.IsDBNull(2) ? null : reader.GetString(2),
                                Album = reader.IsDBNull(3) ? null : reader.GetString(3),
                                Duration = TimeSpan.FromSeconds(reader.GetInt32(4)),
                                FilePath = reader.GetString(5),
                                Genre = reader.IsDBNull(6) ? null : reader.GetString(6),
                                Year = reader.IsDBNull(7) ? null : (int?)reader.GetInt32(7),
                                ArtworkData = reader.IsDBNull(8) ? null : (byte[])reader.GetValue(8),
                                VideoId = reader.IsDBNull(9) ? null : reader.GetString(9),
                                ChannelTitle = reader.IsDBNull(10) ? null : reader.GetString(10),
                                Views = reader.IsDBNull(11) ? null : reader.GetString(11),
                                VideoDuration = reader.IsDBNull(12) ? null : reader.GetString(12)
                            };

                            playlist.Songs.Add(song);
                        }
                    }
                }
            }
            return playlists;
        }

        public void AddSongToPlaylist(long playlistId, Song song)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();

                // First, ensure the song exists in the Songs table
                command.CommandText = @"
        INSERT OR IGNORE INTO Songs (Title, Artist, Album, Duration, FilePath, Genre, Year, ArtworkData, VideoId, ChannelTitle, Views, VideoDuration)
        VALUES (@title, @artist, @album, @duration, @filePath, @genre, @year, @artworkData, @videoId, @channelTitle, @views, @videoDuration);
        SELECT Id FROM Songs WHERE Title = @title AND FilePath = @filePath;";
                command.Parameters.AddWithValue("@title", song.Title);
                command.Parameters.AddWithValue("@artist", song.Artist ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@album", song.Album ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@duration", song.Duration.TotalSeconds);
                command.Parameters.AddWithValue("@filePath", song.FilePath);
                command.Parameters.AddWithValue("@genre", song.Genre ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@year", song.Year ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@artworkData", song.ArtworkData ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@videoId", song.VideoId ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@channelTitle", song.ChannelTitle ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@views", song.Views ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@videoDuration", song.VideoDuration ?? (object)DBNull.Value);
                long songId = (long)command.ExecuteScalar();

                // Now, add the song to the playlist
                command.CommandText = "INSERT OR IGNORE INTO PlaylistSongs (PlaylistId, SongId) VALUES (@playlistId, @songId)";
                command.Parameters.Clear();
                command.Parameters.AddWithValue("@playlistId", playlistId);
                command.Parameters.AddWithValue("@songId", songId);
                command.ExecuteNonQuery();
            }
        }

        public void RemoveSongFromPlaylist(long playlistId, long songId)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();

                command.CommandText = @"
                DELETE FROM PlaylistSongs 
                WHERE PlaylistId = @playlistId AND SongId = @songId";
                command.Parameters.AddWithValue("@playlistId", playlistId);
                command.Parameters.AddWithValue("@songId", songId);
                command.ExecuteNonQuery();
            }
        }

        public long GetPlaylistId(string playlistName)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Id FROM Playlists WHERE Name = @name";
                command.Parameters.AddWithValue("@name", playlistName);

                object? result = command.ExecuteScalar();

                if (result != null && result != DBNull.Value)
                {
                    return Convert.ToInt64(result);
                }

                return -1;
            }
        }
    }
}
