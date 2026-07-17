using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using SoundHaven.Services;

namespace SoundHaven.Tests;

public sealed class LastFmSimilarTracksTests
{
    [Fact]
    public async Task GetSimilarTracks_ParsesTitleAndArtist()
    {
        const string payload = """
            {"similartracks":{"track":[
              {"name":"Similar One","artist":{"name":"Artist A"},
               "image":[{"#text":"https://img.example/l.jpg","size":"extralarge"}]},
              {"name":"Similar Two","artist":{"name":"Artist B"},"image":[]},
              {"name":"","artist":{"name":"No Title"}}
            ]}}
            """;
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var httpClient = new HttpClient(new StubHandler(payload));
        using var service = new LastFmDataService("key", "secret", httpClient, cache);

        var similar = (await service.GetSimilarTracksAsync(
            "Seed Artist",
            "Seed Song",
            8,
            TestContext.Current.CancellationToken)).ToList();

        // Blank-title entries are dropped.
        Assert.Equal(2, similar.Count);
        Assert.Equal("Similar One", similar[0].Title);
        Assert.Equal("Artist A", similar[0].Artist);
        Assert.Equal("Similar Two", similar[1].Title);
        Assert.Equal("Artist B", similar[1].Artist);
    }

    [Fact]
    public async Task GetSimilarTracks_ReturnsEmptyForBlankSeed()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var httpClient = new HttpClient(new StubHandler("""{"similartracks":{"track":[]}}"""));
        using var service = new LastFmDataService("key", "secret", httpClient, cache);

        Assert.Empty(await service.GetSimilarTracksAsync(
            "",
            "",
            8,
            TestContext.Current.CancellationToken));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _payload;

        public StubHandler(string payload)
        {
            _payload = payload;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
