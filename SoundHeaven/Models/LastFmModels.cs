﻿// Models/LastFmModels.cs
using Newtonsoft.Json;
using System.Collections.Generic;

namespace SoundHeaven.Models
{
    // Existing Models

    public class AlbumInfoResponse
    {
        [JsonProperty("album")]
        public LastFmAlbum Album { get; set; }
    }

    public class ArtistInfoResponse
    {
        [JsonProperty("artist")]
        public LastFmArtist Artist { get; set; }

        [JsonProperty("error")]
        public int Error { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    public class TopTracksResponse
    {
        [JsonProperty("tracks")]
        public TracksContainer Tracks { get; set; }
    }

    public class TracksContainer
    {
        [JsonProperty("track")]
        public List<LastFmTrack> TrackList { get; set; }
    }

    public class LastFmAlbum
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("artist")]
        public string Artist { get; set; }

        [JsonProperty("image")]
        public List<LastFmImage> Images { get; set; }

        // Add other properties if needed
    }

    public class TrackInfoResponse
    {
        [JsonProperty("track")]
        public LastFmTrackInfo Track { get; set; }
    }

    public class LastFmTrackInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("album")]
        public LastFmAlbum Album { get; set; }

        // Add other properties if needed
    }

    public class LastFmImage
    {
        [JsonProperty("#text")]
        public string Url { get; set; }

        [JsonProperty("size")]
        public string Size { get; set; }
    }

    public class LastFmTrack
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("duration")]
        public string Duration { get; set; }

        [JsonProperty("artist")]
        public LastFmArtist Artist { get; set; }

        [JsonProperty("image")]
        public List<LastFmImage> Images { get; set; }
    }

    public class LastFmArtist
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("image")]
        public List<LastFmImage> Images { get; set; }
    }

    // New Models Introduced in Previous Response

    // Recent Tracks Models
    public class RecentTracksResponse
    {
        [JsonProperty("recenttracks")]
        public RecentTracks RecentTracks { get; set; }
    }

    public class RecentTracks
    {
        [JsonProperty("track")]
        public List<RecentTrack> TrackList { get; set; }
    }

    public class RecentTrack
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("artist")]
        public SimpleArtist Artist { get; set; }

        [JsonProperty("album")]
        public SimpleAlbum Album { get; set; }

        [JsonProperty("date")]
        public TrackDate Date { get; set; }
    }

    public class SimpleArtist
    {
        [JsonProperty("#text")]
        public string Name { get; set; }
    }

    public class SimpleAlbum
    {
        [JsonProperty("#text")]
        public string Title { get; set; }
    }

    public class TrackDate
    {
        [JsonProperty("uts")]
        public string UnixTimestamp { get; set; }
    }

    // Similar Tracks Models
    public class SimilarTracksResponse
    {
        [JsonProperty("similartracks")]
        public SimilarTracks SimilarTracks { get; set; }
    }

    public class SimilarTracks
    {
        [JsonProperty("track")]
        public List<SimilarTrack> TrackList { get; set; }
    }

    public class SimilarTrack
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("artist")]
        public SimilarTrackArtist Artist { get; set; }

        [JsonProperty("image")]
        public List<LastFmImage> Images { get; set; }
    }

    public class SimilarTrackArtist
    {
        [JsonProperty("name")]
        public string Name { get; set; }
    }

    // Additional Models for Recommended Tracks (if needed)
    // If you plan to extend functionality in the future, consider adding more models here.
}
