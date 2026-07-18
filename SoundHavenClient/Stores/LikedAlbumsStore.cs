using System;
using System.Collections.ObjectModel;
using System.Linq;
using SoundHaven.Data;
using SoundHaven.Models;
using SoundHaven.ViewModels;

namespace SoundHaven.Stores
{
    /// <summary>
    /// The user's liked albums (the album-card heart). Albums are stored by
    /// artist + title with their cover URL, mirrored here as Song objects in the
    /// same shape the home page's album cards use.
    /// </summary>
    public sealed class LikedAlbumsStore : ViewModelBase
    {
        private readonly AppDatabase _appDatabase;

        public LikedAlbumsStore(AppDatabase appDatabase)
        {
            _appDatabase = appDatabase ?? throw new ArgumentNullException(nameof(appDatabase));
            Albums = new ObservableCollection<Song>(_appDatabase.GetLikedAlbums());
            foreach (Song album in Albums)
            {
                album.IsAlbumLiked = true;
            }
        }

        public ObservableCollection<Song> Albums { get; }

        public bool IsLiked(Song? album)
        {
            return album is not null
                && Albums.Any(existing => Matches(existing, album));
        }

        /// <summary>Adds the album if absent, otherwise removes it. Returns the
        /// resulting state (true = now liked).</summary>
        public bool Toggle(Song album)
        {
            ArgumentNullException.ThrowIfNull(album);

            string title = AlbumTitleOf(album);
            if (title.Length == 0)
            {
                return false;
            }

            Song? match = Albums.FirstOrDefault(existing => Matches(existing, album));
            if (match is not null)
            {
                _appDatabase.RemoveLikedAlbum(match.Artist, AlbumTitleOf(match));
                Albums.Remove(match);
                match.IsAlbumLiked = false;
                album.IsAlbumLiked = false;
                return false;
            }

            string? thumbnailUrl = !string.IsNullOrWhiteSpace(album.ThumbnailUrl)
                ? album.ThumbnailUrl
                : album.ArtworkUrl;
            _appDatabase.AddLikedAlbum(album.Artist, title, thumbnailUrl);
            Albums.Add(new Song
            {
                Title = title,
                Album = title,
                Artist = album.Artist,
                ThumbnailUrl = thumbnailUrl,
                ArtworkUrl = thumbnailUrl,
                ArtworkData = album.ArtworkData is { Length: > 0 } artwork
                    ? artwork
                    : Array.Empty<byte>(),
                IsAlbumLiked = true
            });
            album.IsAlbumLiked = true;
            return true;
        }

        private static string AlbumTitleOf(Song album) =>
            album.Album is { Length: > 0 } name ? name : album.Title?.Trim() ?? string.Empty;

        private static bool Matches(Song left, Song right)
        {
            return string.Equals(
                    AlbumTitleOf(left),
                    AlbumTitleOf(right),
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    left.Artist ?? string.Empty,
                    right.Artist ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
        }
    }
}
