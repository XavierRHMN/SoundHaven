using System;
using System.Collections.ObjectModel;
using SoundHaven.Models;
using SoundHaven.ViewModels;

namespace SoundHaven.Stores;

/// <summary>
/// In-memory recently played tracks for the Home screen (not Last.fm).
/// </summary>
public sealed class RecentPlaybackStore : ViewModelBase
{
    public const int MaxItems = 30;

    public ObservableCollection<Song> RecentSongs { get; } = [];

    public void RecordPlay(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);

        Song entry = song.CloneForQueue();
        for (int i = RecentSongs.Count - 1; i >= 0; i--)
        {
            if (IsSameTrack(RecentSongs[i], entry))
            {
                RecentSongs.RemoveAt(i);
            }
        }

        RecentSongs.Insert(0, entry);
        while (RecentSongs.Count > MaxItems)
        {
            RecentSongs.RemoveAt(RecentSongs.Count - 1);
        }

        OnPropertyChanged(nameof(RecentSongs));
    }

    private static bool IsSameTrack(Song left, Song right)
    {
        if (!string.IsNullOrWhiteSpace(left.VideoId)
            && string.Equals(left.VideoId, right.VideoId, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(left.FilePath)
            && string.Equals(left.FilePath, right.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(left.Title, right.Title, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Artist, right.Artist, StringComparison.OrdinalIgnoreCase);
    }
}
