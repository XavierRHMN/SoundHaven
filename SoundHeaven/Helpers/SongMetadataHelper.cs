using System;
using SoundHeaven.Models;

namespace SoundHeaven.Helpers
{
    public static class SongMetadataHelper
    {
        // Format the duration using TimeSpan (e.g., "minutes:seconds")
        public static string FormatDuration(TimeSpan duration)
        {
            return $"{duration.Minutes:D2}:{duration.Seconds:D2}";
        }

        // Format the full song metadata for display purposes
        public static string FormatSongMetadata(Song song)
        {
            if (song == null)
                return string.Empty;

            // Format metadata: "Title - Artist (Album, Year, Genre) [Duration]"
            string year = song.Year > 0 ? song.Year.ToString() : "Unknown Year";
            string genre = !string.IsNullOrWhiteSpace(song.Genre) ? song.Genre : "Unknown Genre";

            return $"{song.Title} - {song.Artist} ({song.Album}, {year}, {genre}) [{FormatDuration(song.Duration)}]";
        }

        // Optional: Helper to format short song metadata for list views or brief summaries
        public static string FormatShortMetadata(Song song)
        {
            return $"{song.Title} - {song.Artist} [{FormatDuration(song.Duration)}]";
        }
    }
}
