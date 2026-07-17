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

        string? url = await service.GetTrackArtworkUrlAsync("Dasha", "Austin");

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

        string? url = await service.GetTrackArtworkUrlAsync("Dasha", "Austin");

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

        Assert.Null(await service.GetTrackArtworkUrlAsync("Nobody", "Nothing"));
        int requestsAfterFirstLookup = handler.RequestedHosts.Count;
        Assert.Null(await service.GetTrackArtworkUrlAsync("Nobody", "Nothing"));

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

        string? url = await service.GetAlbumArtworkUrlAsync("Kenny Chesney", "American Kids");

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

        Assert.Null(await service.GetTrackArtworkUrlAsync("Any", "Song"));
    }

    [Fact]
    public async Task GetTrackArtworkUrl_ReturnsNullForEmptyTitle()
    {
        var handler = new RoutingHandler();
        using var httpClient = new HttpClient(handler);
        var service = new AlbumArtService(httpClient, _cache);

        Assert.Null(await service.GetTrackArtworkUrlAsync("Artist", "   "));
        Assert.Empty(handler.RequestedHosts);
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
            string host = request.RequestUri?.Host ?? string.Empty;
            RequestedHosts.Add(host);

            string body = _responsesByHost.TryGetValue(host, out string? configured)
                ? configured
                : """{"data":[],"results":[]}""";
            var response = new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
