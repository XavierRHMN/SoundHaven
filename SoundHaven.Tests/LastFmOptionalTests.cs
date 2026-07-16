using Microsoft.Extensions.Caching.Memory;
using SoundHaven.Services;

namespace SoundHaven.Tests;

public sealed class LastFmOptionalTests
{
    [Fact]
    public async Task MissingApiCredentials_DisablesLastFmWithoutBreakingTheApp()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var httpClient = new HttpClient();
        using var service = new LastFmDataService(
            string.Empty,
            string.Empty,
            httpClient,
            cache);

        var tracks = await service.GetTopTracksAsync(TestContext.Current.CancellationToken);
        await service.ScrobbleTrackAsync(
            "Title",
            "Artist",
            "Album",
            TestContext.Current.CancellationToken);

        Assert.False(service.IsConfigured);
        Assert.Empty(tracks);
        Assert.NotNull(service.LastError);
    }
}
