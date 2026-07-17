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

        /// <summary>The single irremovable system playlist that backs the heart/favorite
        /// button. Always present and pinned to the top of <see cref="Playlists"/>.</summary>
        public Playlist LikedSongsPlaylist { get; }

        public PlaylistStore(AppDatabase appDatabase)
        {
            _appDatabase = appDatabase ?? throw new ArgumentNullException(nameof(appDatabase));
            _playlists = new ObservableCollection<Playlist>(_appDatabase.GetAllPlaylists());
            LikedSongsPlaylist = EnsureLikedSongsPlaylist();
        }

        private Playlist EnsureLikedSongsPlaylist()
        {
            Playlist? liked = _playlists.FirstOrDefault(playlist => playlist.IsLikedSongs);
            if (liked is null)
            {
                liked = new Playlist { Name = "Liked Songs", IsLikedSongs = true };
                _appDatabase.SavePlaylist(liked);
                liked.CreatedAtUtc ??= DateTime.UtcNow;
                liked.UpdatedAtUtc = DateTime.UtcNow;
                _playlists.Insert(0, liked);
            }
            else
            {
                int index = _playlists.IndexOf(liked);
                if (index > 0)
                {
                    _playlists.Move(index, 0);
                }
            }

            return liked;
        }

        /// <summary>Whether the given song is currently in Liked Songs.</summary>
        public bool IsFavorite(Song? song)
        {
            return song is not null
                && LikedSongsPlaylist.Songs.Any(existing => SongMatches(existing, song));
        }

        /// <summary>Adds the song to Liked Songs if absent, otherwise removes it.
        /// Returns the resulting favorite state (true = now liked).</summary>
        public bool ToggleFavorite(Song song)
        {
            ArgumentNullException.ThrowIfNull(song);

            Song? match = LikedSongsPlaylist.Songs
                .FirstOrDefault(existing => SongMatches(existing, song));
            if (match is not null)
            {
                _appDatabase.RemoveSongFromPlaylist(LikedSongsPlaylist.Id, match.Id);
                LikedSongsPlaylist.Songs.Remove(match);
                LikedSongsPlaylist.UpdatedAtUtc = DateTime.UtcNow;
                return false;
            }

            AddSongToPlaylist(LikedSongsPlaylist, song);
            return true;
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

            // The Liked Songs system playlist is permanent.
            if (playlist.IsLikedSongs)
            {
                return;
            }

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
