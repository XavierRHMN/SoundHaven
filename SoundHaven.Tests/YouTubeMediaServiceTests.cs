using System.Net.Http.Headers;
using NAudio.Wave;
using SoundHaven.Helpers;
using SoundHaven.Services;

namespace SoundHaven.Tests;

public sealed class YouTubeMediaServiceTests
{
    [Theory]
    [InlineData("jNQXAC9IVRw", "jNQXAC9IVRw")]
    [InlineData("https://www.youtube.com/watch?v=jNQXAC9IVRw&t=10", "jNQXAC9IVRw")]
    [InlineData("https://youtu.be/jNQXAC9IVRw?si=example", "jNQXAC9IVRw")]
    [InlineData("https://www.youtube.com/shorts/jNQXAC9IVRw", "jNQXAC9IVRw")]
    [InlineData("https://www.youtube.com/embed/jNQXAC9IVRw", "jNQXAC9IVRw")]
    public void NormalizeVideoId_AcceptsSupportedYouTubeFormats(
        string input,
        string expected)
    {
        using var service = CreateService();

        Assert.Equal(expected, service.NormalizeVideoId(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a video")]
    [InlineData("https://example.com/watch?v=jNQXAC9IVRw")]
    [InlineData("https://youtube.com/watch?v=short")]
    public void NormalizeVideoId_RejectsInvalidValues(string input)
    {
        using var service = CreateService();

        Assert.ThrowsAny<Exception>(() => service.NormalizeVideoId(input));
    }

    [Fact]
    public void SelectCompatibleAudioStream_PrefersHighestBitrateM4aCandidate()
    {
        var lowBitrateM4a = new StreamCandidate("mp4", 96_000);
        var highBitrateM4a = new StreamCandidate("mp4", 160_000);
        var higherBitrateWebM = new StreamCandidate("webm", 256_000);

        StreamCandidate selected = YouTubeMediaService.SelectCompatibleAudioStream(
            [lowBitrateM4a, higherBitrateWebM, highBitrateM4a],
            candidate => candidate.Container == "mp4",
            candidate => candidate.Bitrate);

        Assert.Same(highBitrateM4a, selected);
    }

    [Fact]
    public void SelectCompatibleAudioStream_RejectsIncompatibleCandidates()
    {
        var candidates = new[]
        {
            new StreamCandidate("webm", 128_000),
            new StreamCandidate("ogg", 192_000)
        };

        Assert.Throws<InvalidOperationException>(() =>
            YouTubeMediaService.SelectCompatibleAudioStream(
                candidates,
                candidate => candidate.Container == "mp4",
                candidate => candidate.Bitrate));
    }

    [Fact]
    public void GetStableThumbnailUrl_FallsBackToMaxResWhenNoPreferredUrlExists()
    {
        string? url = YouTubeMediaService.GetStableThumbnailUrl(
            "jNQXAC9IVRw",
            Array.Empty<YoutubeExplode.Common.Thumbnail>());

        Assert.Equal("https://i.ytimg.com/vi/jNQXAC9IVRw/maxresdefault.jpg", url);
    }

    [Theory]
    [InlineData(
        "https://i.ytimg.com/vi/jNQXAC9IVRw/hqdefault.jpg",
        "https://i.ytimg.com/vi/jNQXAC9IVRw/maxresdefault.jpg")]
    [InlineData(
        "https://lh3.googleusercontent.com/abc=w120-h120-l90-rj",
        "https://lh3.googleusercontent.com/abc=w1200-h1200-l90-rj")]
    [InlineData(
        "https://lh3.googleusercontent.com/abc=s60-c-k-c0x00ffffff-no-rj",
        "https://lh3.googleusercontent.com/abc=s1200-c-k-c0x00ffffff-no-rj")]
    public void UpgradeThumbnailUrl_PrefersHigherResolutionSources(string input, string expected)
    {
        Assert.Equal(expected, YouTubeThumbnailHelper.UpgradeThumbnailUrl(input));
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task SearchResolveSeekAndCache_ReturnsPlayableM4aSource_WhenLiveTestsEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("SOUNDHAVEN_RUN_LIVE_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        using var service = CreateService();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        IReadOnlyList<YouTubeSearchResult> results = await service.SearchAsync(
            "Me at the zoo jawed",
            limit: 10,
            searchSongs: false,
            cancellation.Token);
        YouTubeSearchResult result = Assert.Single(
            results,
            candidate => candidate.VideoId == "jNQXAC9IVRw");

        YouTubeStreamSource source = await service.ResolveStreamAsync(
            result.VideoId,
            cancellation.Token);

        Assert.Equal("mp4", source.Container, ignoreCase: true);
        Assert.True(source.StreamUri.IsAbsoluteUri);
        Assert.True(source.Bitrate > 0);

        using var reader = new MediaFoundationReader(source.StreamUri.AbsoluteUri);
        byte[] buffer = new byte[Math.Max(reader.WaveFormat.AverageBytesPerSecond / 4, 4096)];
        foreach (double seekSeconds in new[] { 2d, 5d, 1d })
        {
            reader.CurrentTime = TimeSpan.FromSeconds(seekSeconds);
            int bytesRead = reader.Read(buffer, 0, buffer.Length);

            Assert.True(bytesRead > 0);
            Assert.True(reader.CurrentTime >= TimeSpan.FromSeconds(seekSeconds - 1));
        }

        string cachedPath = await service.CacheAudioAsync(
            source.VideoId,
            cancellationToken: cancellation.Token);
        try
        {
            Assert.EndsWith(".m4a", cachedPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(new FileInfo(cachedPath).Length > 0);

            using var cachedReader = new MediaFoundationReader(cachedPath);
            cachedReader.CurrentTime = TimeSpan.FromSeconds(3);
            int cachedBytesRead = cachedReader.Read(buffer, 0, buffer.Length);

            Assert.True(cachedBytesRead > 0);
            Assert.True(cachedReader.CurrentTime >= TimeSpan.FromSeconds(2));
        }
        finally
        {
            File.Delete(cachedPath);
        }
    }

    [Fact]
    public async Task GetHomeRecommendationsAsync_ReturnsPlayableSongsFromHomePlaylists()
    {
        using var service = CreateService();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        IReadOnlyList<YouTubeSearchResult> results;
        try
        {
            results = await service.GetHomeRecommendationsAsync(
                limit: 8,
                cancellationToken: cancellation.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // Offline / blocked CI — skip without failing the suite.
            return;
        }

        Assert.NotEmpty(results);
        Assert.All(results, result =>
        {
            Assert.False(string.IsNullOrWhiteSpace(result.VideoId));
            Assert.False(string.IsNullOrWhiteSpace(result.Title));
        });
    }

    private static YouTubeMediaService CreateService()
    {
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
            + "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        return new YouTubeMediaService(httpClient);
    }

    private sealed record StreamCandidate(string Container, long Bitrate);
}
