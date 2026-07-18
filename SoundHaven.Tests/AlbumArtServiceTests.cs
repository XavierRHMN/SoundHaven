using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using SoundHaven.Services;

namespace SoundHaven.Tests;

public sealed class AlbumArtServiceTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    [Fact]
    public async Task GetTrackArtworkUrl_PrefersDeezerCover()
    {
        var handler = new RoutingHandler
        {
            ["api.deezer.com"] =
                """{"data":[{"album":{"cover_xl":"https://cdn.deezer.example/xl.jpg"}}]}"""
        };
        using var httpClient = new HttpClient(handler);
        var service = new AlbumArtService(httpClient, _cache);

        string? url = await service.GetTrackArtworkUrlAsync(
            "Dasha",
            "Austin",
            TestContext.Current.CancellationToken);

        Assert.Equal("https://cdn.deezer.example/xl.jpg", url);
        Assert.DoesNotContain(handler.RequestedHosts, host => host.Contains("itunes"));
    }

    [Fact]
    public async Task GetTrackArtworkUrl_FallsBackToITunesAndUpgradesSize()
    {
        var handler = new RoutingHandler
        {
            ["api.deezer.com"] = """{"data":[]}""",
            ["itunes.apple.com"] =
                """{"resultCount":1,"results":[{"artworkUrl100":"https://is1.mzstatic.example/img/100x100bb.jpg"}]}"""
        };
        using var httpClient = new HttpClient(handler);
        var service = new AlbumArtService(httpClient, _cache);

        string? url = await service.GetTrackArtworkUrlAsync(
            "Dasha",
            "Austin",
            TestContext.Current.CancellationToken);

        Assert.Equal("https://is1.mzstatic.example/img/600x600bb.jpg", url);
    }

    [Fact]
    public async Task GetTrackArtworkUrl_CachesMissesWithoutRequerying()
    {
        var handler = new RoutingHandler
        {
            ["api.deezer.com"] = """{"data":[]}""",
            ["itunes.apple.com"] = """{"resultCount":0,"results":[]}"""
        };
        using var httpClient = new HttpClient(handler);
        var service = new AlbumArtService(httpClient, _cache);

        Assert.Null(await service.GetTrackArtworkUrlAsync(
            "Nobody",
            "Nothing",
            TestContext.Current.CancellationToken));
        int requestsAfterFirstLookup = handler.RequestedHosts.Count;
        Assert.Null(await service.GetTrackArtworkUrlAsync(
            "Nobody",
            "Nothing",
            TestContext.Current.CancellationToken));

        Assert.Equal(requestsAfterFirstLookup, handler.RequestedHosts.Count);
    }

    [Fact]
    public async Task GetTrackYear_ReadsITunesReleaseDate()
    {
        var handler = new RoutingHandler
        {
            ["itunes.apple.com"] =
                """{"resultCount":1,"results":[{"releaseDate":"2010-11-22T08:00:00Z"}]}"""
        };
        using var httpClient = new HttpClient(handler);
        var service = new AlbumArtService(httpClient, _cache);

        int? year = await service.GetTrackYearAsync(
            "Kanye West",
            "Runaway",
            TestContext.Current.CancellationToken);

        Assert.Equal(2010, year);
    }

    [Fact]
    public async Task GetTrackYear_CachesMissesWithoutRequerying()
    {
        var handler = new RoutingHandler
        {
            ["itunes.apple.com"] = """{"resultCount":0,"results":[]}"""
        };
        using var httpClient = new HttpClient(handler);
        var service = new AlbumArtService(httpClient, _cache);

        Assert.Null(await service.GetTrackYearAsync(
            "Nobody",
            "Nothing",
            TestContext.Current.CancellationToken));
        int requestsAfterFirstLookup = handler.RequestedHosts.Count;
        Assert.Null(await service.GetTrackYearAsync(
            "Nobody",
            "Nothing",
            TestContext.Current.CancellationToken));

        Assert.Equal(requestsAfterFirstLookup, handler.RequestedHosts.Count);
    }

    [Fact]
    public async Task GetAlbumArtworkUrl_ReadsAlbumCoverDirectly()
    {
        var handler = new RoutingHandler
        {
            ["api.deezer.com"] =
                """{"data":[{"cover_xl":"https://cdn.deezer.example/album-xl.jpg"}]}"""
        };
        using var httpClient = new HttpClient(handler);
        var service = new AlbumArtService(httpClient, _cache);

        string? url = await service.GetAlbumArtworkUrlAsync(
            "Kenny Chesney",
            "American Kids",
            TestContext.Current.CancellationToken);

        Assert.Equal("https://cdn.deezer.example/album-xl.jpg", url);
    }

    [Fact]
    public async Task GetTrackArtworkUrl_ReturnsNullOnHttpFailure()
    {
        var handler = new RoutingHandler
        {
            StatusCode = HttpStatusCode.InternalServerError
        };
        using var httpClient = new HttpClient(handler);
        var service = new AlbumArtService(httpClient, _cache);

        Assert.Null(await service.GetTrackArtworkUrlAsync(
            "Any",
            "Song",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetTrackArtworkUrl_ReturnsNullForEmptyTitle()
    {
        var handler = new RoutingHandler();
        using var httpClient = new HttpClient(handler);
        var service = new AlbumArtService(httpClient, _cache);

        Assert.Null(await service.GetTrackArtworkUrlAsync(
            "Artist",
            "   ",
            TestContext.Current.CancellationToken));
        Assert.Empty(handler.RequestedHosts);
    }

    [Fact]
    public async Task GetAlbumWithTracks_PicksExactTitleMatchOverPartial()
    {
        var handler = new RoutingHandler
        {
            // Fuzzy search returns a partial-title hit first; the exact title with a
            // matching artist must win, and its own tracks/cover must be used.
            ["api.deezer.com/search/album"] = """
                {"data":[
                  {"id":11,"title":"Music Is My Drug","cover_xl":"https://cdn.deezer.example/wrong.jpg","artist":{"name":"Playboi Carti"}},
                  {"id":42,"title":"MUSIC","cover_xl":"https://cdn.deezer.example/music-xl.jpg","artist":{"name":"Playboi Carti"}}
                ]}
                """,
            ["api.deezer.com/album/42/tracks"] = """
                {"data":[
                  {"title":"POP OUT","duration":163,"artist":{"name":"Playboi Carti"}},
                  {"title":"K POP","duration":141,"artist":{"name":"Playboi Carti"}}
                ]}
                """,
            ["api.deezer.com/album/42?"] = """{"id":42,"release_date":"2025-03-14"}"""
        };
        using var httpClient = new HttpClient(handler);
        var service = new AlbumArtService(httpClient, _cache);

        ResolvedAlbum? resolved = await service.GetAlbumWithTracksAsync(
            "Playboi Carti",
            "Music",
            TestContext.Current.CancellationToken);

        Assert.NotNull(resolved);
        Assert.Equal("https://cdn.deezer.example/music-xl.jpg", resolved.CoverUrl);
        List<string> expectedTitles = ["POP OUT", "K POP"];
        Assert.Equal(expectedTitles, resolved.Tracks.Select(track => track.Title));
        Assert.Equal(TimeSpan.FromSeconds(163), resolved.Tracks[0].Duration);
        // The album's release year applies to every track.
        Assert.All(resolved.Tracks, track => Assert.Equal(2025, track.Year));
    }

    [Fact]
    public async Task GetAlbumWithTracks_RejectsWrongArtistEverywhere()
    {
        var handler = new RoutingHandler
        {
            ["api.deezer.com/search/album"] = """
                {"data":[{"id":7,"title":"Music","cover_xl":"https://cdn.deezer.example/other.jpg","artist":{"name":"Someone Else"}}]}
                """,
            ["itunes.apple.com/search"] = """
                {"resultCount":1,"results":[{"wrapperType":"collection","collectionId":9,"collectionName":"Music","artistName":"Another Artist","artworkUrl100":"https://is1.mzstatic.example/100x100bb.jpg"}]}
                """
        };
        using var httpClient = new HttpClient(handler);
        var service = new AlbumArtService(httpClient, _cache);

        Assert.Null(await service.GetAlbumWithTracksAsync(
            "Playboi Carti",
            "Music",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAlbumWithTracks_FallsBackToITunesWithYearsAndOrder()
    {
        var handler = new RoutingHandler
        {
            ["api.deezer.com/search/album"] = """{"data":[]}""",
            ["itunes.apple.com/search"] = """
                {"resultCount":1,"results":[{"wrapperType":"collection","collectionId":9,"collectionName":"Graduation","artistName":"Kanye West","artworkUrl100":"https://is1.mzstatic.example/100x100bb.jpg"}]}
                """,
            ["itunes.apple.com/lookup"] = """
                {"resultCount":3,"results":[
                  {"wrapperType":"collection","collectionId":9,"collectionName":"Graduation"},
                  {"wrapperType":"track","trackName":"Champion","artistName":"Kanye West","trackTimeMillis":167000,"trackNumber":2,"discNumber":1,"releaseDate":"2007-09-11T07:00:00Z"},
                  {"wrapperType":"track","trackName":"Good Morning","artistName":"Kanye West","trackTimeMillis":195000,"trackNumber":1,"discNumber":1,"releaseDate":"2007-09-11T07:00:00Z"}
                ]}
                """
        };
        using var httpClient = new HttpClient(handler);
        var service = new AlbumArtService(httpClient, _cache);

        ResolvedAlbum? resolved = await service.GetAlbumWithTracksAsync(
            "Kanye West",
            "Graduation",
            TestContext.Current.CancellationToken);

        Assert.NotNull(resolved);
        Assert.Equal("https://is1.mzstatic.example/600x600bb.jpg", resolved.CoverUrl);
        List<string> expectedTitles = ["Good Morning", "Champion"];
        Assert.Equal(expectedTitles, resolved.Tracks.Select(track => track.Title));
        Assert.Equal(2007, resolved.Tracks[0].Year);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(
        "https://lastfm.freetls.fastly.net/i/u/300x300/2a96cbd8b46e442fc41c2b86b821562f.png",
        false)]
    [InlineData("https://lastfm.freetls.fastly.net/i/u/300x300/abc123.png", true)]
    public void IsUsableArtworkUrl_RejectsPlaceholders(string? url, bool expected)
    {
        Assert.Equal(expected, AlbumArtService.IsUsableArtworkUrl(url));
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responsesByHost = new(StringComparer.OrdinalIgnoreCase);

        public List<string> RequestedHosts { get; } = [];

        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

        public string this[string host]
        {
            set => _responsesByHost[host] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            RequestedHosts.Add(request.RequestUri?.Host ?? string.Empty);

            // Keys are URL substrings (a bare host, or host + path for endpoints
            // that need distinct responses); the most specific match wins.
            string? body = null;
            int bestLength = -1;
            foreach (KeyValuePair<string, string> pair in _responsesByHost)
            {
                if (pair.Key.Length > bestLength
                    && uri.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
                {
                    body = pair.Value;
                    bestLength = pair.Key.Length;
                }
            }

            var response = new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(
                    body ?? """{"data":[],"results":[]}""",
                    Encoding.UTF8,
                    "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
