using System;
using System.Collections.Generic;
using SoundHaven.Data;
using SoundHaven.Models;

namespace SoundHaven.Stores;

/// <summary>
/// Locally persisted "not interested" list used to filter recommendations.
/// Matches by videoId when available, otherwise by title + artist.
/// </summary>
public sealed class DislikedSongsStore
{
    private readonly AppDatabase _database;
    private readonly HashSet<string> _videoIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _trackKeys = new(StringComparer.OrdinalIgnoreCase);

    public DislikedSongsStore(AppDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        foreach (DislikedSong entry in _database.GetDislikedSongs())
        {
            Remember(entry.VideoId, entry.Title, entry.Artist);
        }
    }

    public void Dislike(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        string title = song.Title?.Trim() ?? string.Empty;
        if (title.Length == 0 || IsDisliked(song))
        {
            return;
        }

        _database.AddDislikedSong(song.VideoId, title, song.Artist);
        Remember(song.VideoId, title, song.Artist);
    }

    public bool IsDisliked(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        return IsDisliked(song.VideoId, song.Title, song.Artist);
    }

    public bool IsDisliked(string? videoId, string? title, string? artist)
    {
        if (!string.IsNullOrWhiteSpace(videoId) && _videoIds.Contains(videoId))
        {
            return true;
        }

        string key = BuildKey(title, artist);
        return key.Length > 0 && _trackKeys.Contains(key);
    }

    private void Remember(string? videoId, string title, string? artist)
    {
        if (!string.IsNullOrWhiteSpace(videoId))
        {
            _videoIds.Add(videoId);
        }

        string key = BuildKey(title, artist);
        if (key.Length > 0)
        {
            _trackKeys.Add(key);
        }
    }

    private static string BuildKey(string? title, string? artist)
    {
        return string.IsNullOrWhiteSpace(title)
            ? string.Empty
            : $"{title.Trim()}|{artist?.Trim()}";
    }
}
