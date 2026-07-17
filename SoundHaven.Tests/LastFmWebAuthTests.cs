using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using SoundHaven.Services;

namespace SoundHaven.Tests;

public sealed class LastFmWebAuthTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "SoundHaven.Tests",
        Guid.NewGuid().ToString("N"));

    private string SessionPath => Path.Combine(_directory, "lastfm-session.bin");

    [Fact]
    public async Task StartWebAuth_ReturnsTokenAndApprovalUrl()
    {
        var handler = new SequenceHandler("""{"token":"tok123"}""");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var httpClient = new HttpClient(handler);
        using var service = new LastFmDataService("key", "secret", httpClient, cache, SessionPath);

        LastFmWebAuth auth = await service.StartWebAuthAsync(TestContext.Current.CancellationToken);

        Assert.Equal("tok123", auth.Token);
        Assert.Contains("api_key=key", auth.ApprovalUrl);
        Assert.Contains("token=tok123", auth.ApprovalUrl);
    }

    [Fact]
    public async Task WaitForWebAuth_LinksAccountAndPersistsSession()
    {
        var handler = new SequenceHandler(
            """{"error":14,"message":"This token has not been authorized"}""",
            """{"session":{"name":"Xavier","key":"sess-key-1"}}""");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var httpClient = new HttpClient(handler);
        using var service = new LastFmDataService("key", "secret", httpClient, cache, SessionPath)
        {
            WebAuthPollInterval = TimeSpan.FromMilliseconds(1)
        };

        bool linked = await service.WaitForWebAuthAsync(
            new LastFmWebAuth("tok123", "https://www.last.fm/api/auth/"),
            TestContext.Current.CancellationToken);

        Assert.True(linked);
        Assert.True(service.IsAuthenticated);
        Assert.Equal("Xavier", service.Username);

        // A fresh service reloads the persisted session.
        using var reloaded = new LastFmDataService("key", "secret", httpClient, cache, SessionPath);
        Assert.True(reloaded.IsAuthenticated);
        Assert.Equal("Xavier", reloaded.Username);
    }

    [Fact]
    public async Task SignOut_ClearsPersistedSession()
    {
        var handler = new SequenceHandler(
            """{"session":{"name":"Xavier","key":"sess-key-1"}}""");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var httpClient = new HttpClient(handler);
        using var service = new LastFmDataService("key", "secret", httpClient, cache, SessionPath)
        {
            WebAuthPollInterval = TimeSpan.FromMilliseconds(1)
        };
        Assert.True(await service.WaitForWebAuthAsync(
            new LastFmWebAuth("tok123", "https://www.last.fm/api/auth/"),
            TestContext.Current.CancellationToken));

        service.SignOut();

        Assert.False(service.IsAuthenticated);
        using var reloaded = new LastFmDataService("key", "secret", httpClient, cache, SessionPath);
        Assert.False(reloaded.IsAuthenticated);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public SequenceHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string payload = _responses.Count > 0
                ? _responses.Dequeue()
                : """{"error":14,"message":"pending"}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
