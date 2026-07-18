using System;
using System.Collections.ObjectModel;
using System.IO;
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

        /// <summary>The single irremovable system playlist mirroring every offline
        /// download. Membership is derived: downloads add themselves, removals and
        /// vanished files drop out, and startup reconciles against the disk.</summary>
        public Playlist DownloadedSongsPlaylist { get; }

        public PlaylistStore(AppDatabase appDatabase)
        {
            _appDatabase = appDatabase ?? throw new ArgumentNullException(nameof(appDatabase));
            _playlists = new ObservableCollection<Playlist>(_appDatabase.GetAllPlaylists());
            LikedSongsPlaylist = EnsureLikedSongsPlaylist();
            DownloadedSongsPlaylist = EnsureDownloadedSongsPlaylist();
            ReconcileDownloads();
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

        private Playlist EnsureDownloadedSongsPlaylist()
        {
            Playlist? downloads = _playlists.FirstOrDefault(playlist => playlist.IsDownloads);
            if (downloads is null)
            {
                downloads = new Playlist { Name = "Downloaded Songs", IsDownloads = true };
                _appDatabase.SavePlaylist(downloads);
                downloads.CreatedAtUtc ??= DateTime.UtcNow;
                downloads.UpdatedAtUtc = DateTime.UtcNow;
                _playlists.Insert(Math.Min(1, _playlists.Count), downloads);
            }
            else
            {
                int index = _playlists.IndexOf(downloads);
                int target = Math.Min(1, _playlists.Count - 1);
                if (index != target)
                {
                    _playlists.Move(index, target);
                }
            }

            return downloads;
        }

        // Startup pass that makes Downloaded Songs match the disk: songs whose file
        // still exists get their runtime state stamped (DownloadState isn't persisted)
        // and join the playlist; members whose file vanished drop out.
        private void ReconcileDownloads()
        {
            foreach (Song member in DownloadedSongsPlaylist.Songs.ToList())
            {
                if (!IsDownloadedFile(member))
                {
                    _appDatabase.RemoveSongFromPlaylist(DownloadedSongsPlaylist.Id, member.Id);
                    DownloadedSongsPlaylist.Songs.Remove(member);
                }
            }

            foreach (Playlist playlist in _playlists)
            {
                foreach (Song song in playlist.Songs.ToList())
                {
                    if (string.IsNullOrWhiteSpace(song.FilePath) || !File.Exists(song.FilePath))
                    {
                        continue;
                    }

                    song.CurrentDownloadState = DownloadState.Downloaded;
                    song.DownloadProgress = 100;

                    if (IsDownloadedFile(song)
                        && !DownloadedSongsPlaylist.Songs.Any(existing => SongMatches(existing, song)))
                    {
                        AddSongToPlaylist(DownloadedSongsPlaylist, song);
                    }
                }
            }
        }

        // Only YouTube-backed files count as downloads: local files the user imported
        // play offline anyway and can't be re-streamed if their file were removed.
        private static bool IsDownloadedFile(Song song) =>
            !string.IsNullOrWhiteSpace(song.VideoId)
            && !string.IsNullOrWhiteSpace(song.FilePath)
            && File.Exists(song.FilePath);

        /// <summary>Records a completed download: joins Downloaded Songs (persisting the
        /// song if it wasn't stored yet) and saves the file path + video id.</summary>
        public void MarkDownloaded(Song song)
        {
            ArgumentNullException.ThrowIfNull(song);

            if (string.IsNullOrWhiteSpace(song.FilePath))
            {
                return;
            }

            if (!DownloadedSongsPlaylist.Songs.Any(existing => SongMatches(existing, song)))
            {
                AddSongToPlaylist(DownloadedSongsPlaylist, song);
            }

            if (song.Id > 0)
            {
                _appDatabase.UpdateSongDownload(song.Id, song.FilePath, song.VideoId);
            }
        }

        /// <summary>Records a removed download: leaves Downloaded Songs and clears the
        /// stored file path so the song streams again next session.</summary>
        public void MarkUndownloaded(Song song)
        {
            ArgumentNullException.ThrowIfNull(song);

            Song? match = DownloadedSongsPlaylist.Songs
                .FirstOrDefault(existing => SongMatches(existing, song));
            if (match is not null)
            {
                _appDatabase.RemoveSongFromPlaylist(DownloadedSongsPlaylist.Id, match.Id);
                DownloadedSongsPlaylist.Songs.Remove(match);
                DownloadedSongsPlaylist.UpdatedAtUtc = DateTime.UtcNow;

                if (!ReferenceEquals(match, song))
                {
                    match.FilePath = null;
                    match.CurrentDownloadState = DownloadState.NotDownloaded;
                    match.DownloadProgress = 0;
                }
            }

            long songId = song.Id > 0 ? song.Id : match?.Id ?? 0;
            if (songId > 0)
            {
                _appDatabase.UpdateSongFilePath(songId, null);
            }
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

            // The Liked / Downloaded system playlists are permanent.
            if (playlist.IsSystemPlaylist)
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
