
using Newtonsoft.Json;
using System.Collections.Generic;

namespace SoundHaven.Models
{
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
    
    public class TrackSearchResponse
    {
        [JsonProperty("results")]
        public TrackSearchResults Results { get; set; }
    }
    
    public class TrackSearchResults
    {
        [JsonProperty("trackmatches")]
        public TrackMatches TrackMatches { get; set; }
    }
    
    public class TrackMatches
    {
        [JsonProperty("track")]
        public List<TrackSearchResult> TrackList { get; set; }
    }

    public class TrackSearchResult
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("artist")]
        public string Artist { get; set; }

    }
    
    public class UserTopTracksResponse
    {
        [JsonProperty("toptracks")]
        public UserTopTracks TopTracks { get; set; }
    }

    public class UserTopTracks
    {
        [JsonProperty("track")]
        public List<UserTopTrack> TrackList { get; set; }

        [JsonProperty("@attr")]
        public TopTracksAttr Attr { get; set; }
    }

    public class UserTopTrack
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("playcount")]
        public int PlayCount { get; set; }

        [JsonProperty("artist")]
        public SimpleArtist Artist { get; set; }

        [JsonProperty("duration")]
        public string Duration { get; set; }
        
    }
    
    public class TopTracksAttr
    {
        [JsonProperty("user")]
        public string User { get; set; }

        [JsonProperty("page")]
        public string Page { get; set; }

        [JsonProperty("perPage")]
        public string PerPage { get; set; }

        [JsonProperty("totalPages")]
        public string TotalPages { get; set; }

        [JsonProperty("total")]
        public string Total { get; set; }
    }
    
    public class SimilarArtistsResponse
    {
        [JsonProperty("similarartists")]
        public SimilarArtists SimilarArtists { get; set; }
    }

    public class SimilarArtists
    {
        [JsonProperty("artist")]
        public List<SimilarArtist> ArtistList { get; set; }
    }

    public class SimilarArtist
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        
    }

    public class UserTopArtistsResponse
    {
        [JsonProperty("topartists")]
        public UserTopArtists TopArtists { get; set; }
    }
    
    public class UserTopArtists
    {
        [JsonProperty("artist")]
        public List<UserTopArtist> ArtistList { get; set; }
    }
    
    public class UserTopArtist
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        
    }
    
    public class ArtistTopTracksResponse
    {
        [JsonProperty("toptracks")]
        public ArtistTopTracks TopTracks { get; set; }
    }
    
    public class ArtistTopTracks
    {
        [JsonProperty("track")]
        public List<ArtistTopTrack> TrackList { get; set; }
    }
    
    public class ArtistTopTrack
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("duration")]
        public string Duration { get; set; }
    }
}
