using System;
using System.Collections.ObjectModel;
using System.Linq;
using SoundHaven.Data;
using SoundHaven.Models;
using SoundHaven.ViewModels;

namespace SoundHaven.Stores
{
    public class PlaylistStore : ViewModelBase
    {
        private readonly AppDatabase _appDatabase;
        private readonly ObservableCollection<Playlist> _playlists;

        public ObservableCollection<Playlist> Playlists => _playlists;

        public PlaylistStore(AppDatabase appDatabase)
        {
            _appDatabase = appDatabase ?? throw new ArgumentNullException(nameof(appDatabase));
            _playlists = new ObservableCollection<Playlist>(_appDatabase.GetAllPlaylists());
        }

        public void AddPlaylist(Playlist playlist)
        {
            ArgumentNullException.ThrowIfNull(playlist);

            if (Playlists.Contains(playlist))
            {
                return;
            }

            _appDatabase.SavePlaylist(playlist);
            playlist.CreatedAtUtc ??= DateTime.UtcNow;
            playlist.UpdatedAtUtc = DateTime.UtcNow;
            if (!Playlists.Any(existing => existing.Id == playlist.Id))
            {
                Playlists.Add(playlist);
            }
        }

        public void AddSongToPlaylist(Playlist playlist, Song song)
        {
            ArgumentNullException.ThrowIfNull(playlist);
            ArgumentNullException.ThrowIfNull(song);

            if (playlist.Id <= 0)
            {
                throw new InvalidOperationException("Save the playlist before adding songs.");
            }

            _appDatabase.AddSongToPlaylist(playlist.Id, song);

            Playlist? stored = Playlists.FirstOrDefault(existing =>
                ReferenceEquals(existing, playlist)
                || (playlist.Id > 0 && existing.Id == playlist.Id));

            (stored ?? playlist).UpdatedAtUtc = DateTime.UtcNow;

            ObservableCollection<Song> targetSongs = stored?.Songs ?? playlist.Songs;
            if (targetSongs.Any(existing => SongMatches(existing, song)))
            {
                return;
            }

            targetSongs.Add(song);
        }

        private static bool SongMatches(Song left, Song right)
        {
            if (left.Id > 0 && left.Id == right.Id)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(right.VideoId)
                && string.Equals(left.VideoId, right.VideoId, StringComparison.Ordinal))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(right.FilePath)
                && string.Equals(left.FilePath, right.FilePath, StringComparison.OrdinalIgnoreCase);
        }

        public void RemovePlaylist(Playlist playlist)
        {
            ArgumentNullException.ThrowIfNull(playlist);

            Playlist? storedPlaylist = Playlists.FirstOrDefault(existing =>
                ReferenceEquals(existing, playlist) ||
                (playlist.Id > 0 && existing.Id == playlist.Id));

            _appDatabase.RemovePlaylist(playlist);

            if (storedPlaylist != null)
            {
                Playlists.Remove(storedPlaylist);
                storedPlaylist.Id = 0;
            }

            playlist.Id = 0;
        }
    }
}
