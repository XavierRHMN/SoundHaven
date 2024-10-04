using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Collections.ObjectModel;
using SoundHaven.Models;

namespace SoundHaven.Data
{
    public class MusicDatabase
    {
        private string connectionString;

        public MusicDatabase(string dbPath)
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
                        ThumbnailUrl TEXT,
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
                    );";
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

                // Load songs for each playlist
                foreach (var playlist in playlists)
                {
                    command.CommandText = @"
                        SELECT s.Id, s.Title, s.Artist, s.Album, s.Duration
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
                                Artist = reader.GetString(2),
                                Album = reader.GetString(3),
                                Duration = reader.GetTimeSpan(4)
                            };
                            playlist.Songs.Add(song);
                        }
                    }
                }
            }
            return playlists;
        }
    }
}