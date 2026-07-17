using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SoundHaven.Helpers;
using SoundHaven.Models;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace SoundHaven.Services;

public sealed record YouTubeSearchResult(
    string VideoId,
    string Title,
    string Author,
    string? Album,
    TimeSpan? Duration,
    string? ThumbnailUrl,
    long ViewCount,
    int? Year);

public sealed record YouTubeStreamSource(
    string VideoId,
    Uri StreamUri,
    TimeSpan Duration,
    string Container,
    long Bitrate,
    string Title);

public interface IYouTubeMediaService : IDisposable
{
    Task<IReadOnlyList<YouTubeSearchResult>> SearchAsync(
        string query,
        int limit,
        bool searchSongs,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<YouTubeSearchResult>> GetHomeRecommendationsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<YouTubeStreamSource> ResolveStreamAsync(
        string videoId,
        CancellationToken cancellationToken = default);

    Task<Song> DownloadAudioAsync(
        string videoId,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    Task<string> CacheAudioAsync(
        string videoId,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    string NormalizeVideoId(string value);
}

public sealed class YouTubeMediaService : IYouTubeMediaService
{
    private const int MetadataConcurrency = 4;
    private readonly YoutubeClient _youtubeClient;
    private readonly HttpClient _httpClient;
    private readonly YouTubeMusicSearchClient _youTubeMusicSearch;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public YouTubeMediaService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _youtubeClient = new YoutubeClient();
        _youTubeMusicSearch = new YouTubeMusicSearchClient(_httpClient);
    }

    public async Task<IReadOnlyList<YouTubeSearchResult>> SearchAsync(
        string query,
        int limit,
        bool searchSongs,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<YouTubeSearchResult>();
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        if (searchSongs)
        {
            try
            {
                return await _youTubeMusicSearch
                    .SearchSongsAsync(query, limit, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                // Fall back to general YouTube video search if Music catalogue search fails.
            }
        }

        return await SearchVideosAsync(query, limit, searchSongs, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<YouTubeSearchResult>> GetHomeRecommendationsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        try
        {
            return await _youTubeMusicSearch
                .BrowseHomeSongsAsync(limit, cancellationToken)
                .ConfigureAwait(false);
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            return Array.Empty<YouTubeSearchResult>();
        }
    }

    private async Task<IReadOnlyList<YouTubeSearchResult>> SearchVideosAsync(
        string query,
        int limit,
        bool preferMusicLikeResults,
        CancellationToken cancellationToken)
    {
        string trimmedQuery = query.Trim();
        string effectiveQuery = preferMusicLikeResults
            ? $"{trimmedQuery} official audio"
            : trimmedQuery;

        int fetchCount = preferMusicLikeResults ? Math.Min(limit * 3, 45) : limit;
        var searchResults = await _youtubeClient.Search
            .GetVideosAsync(effectiveQuery, cancellationToken)
            .CollectAsync(fetchCount);

        IEnumerable<YoutubeExplode.Search.VideoSearchResult> ranked = preferMusicLikeResults
            ? searchResults
                .OrderBy(result => ScoreSongCandidate(result.Duration, result.Title))
                .ThenByDescending(result => result.Thumbnails.Count)
                .Take(limit)
            : searchResults.Take(limit);

        using var gate = new SemaphoreSlim(MetadataConcurrency, MetadataConcurrency);
        var tasks = ranked.Select(async result =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Video? details = null;
                try
                {
                    details = await _youtubeClient.Videos
                        .GetAsync(result.Id, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch when (!cancellationToken.IsCancellationRequested)
                {
                    // Search results are still useful if an enrichment request fails.
                }

                string? thumbnailUrl = GetStableThumbnailUrl(result.Id, result.Thumbnails)
                    ?? GetStableThumbnailUrl(
                        result.Id,
                        details?.Thumbnails ?? Array.Empty<Thumbnail>());

                return new YouTubeSearchResult(
                    result.Id,
                    result.Title,
                    result.Author.ChannelTitle,
                    null,
                    result.Duration ?? details?.Duration,
                    thumbnailUrl,
                    details?.Engagement.ViewCount ?? 0,
                    details?.UploadDate.Year);
            }
            finally
            {
                gate.Release();
            }
        });

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    internal static string? GetStableThumbnailUrl(
        string videoId,
        IEnumerable<Thumbnail> thumbnails)
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return null;
        }

        // Prefer YoutubeExplode's largest thumbnail when it already points at i.ytimg.com,
        // then upgrade hq/sd links to maxres when possible.
        string? preferred = thumbnails
            .OrderByDescending(thumbnail => thumbnail.Resolution.Area)
            .Select(thumbnail => thumbnail.Url)
            .FirstOrDefault(url =>
                !string.IsNullOrWhiteSpace(url)
                && url.Contains("i.ytimg.com", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return YouTubeThumbnailHelper.UpgradeThumbnailUrl(preferred);
        }

        // Stable CDN URLs avoid signed ggpht/googleusercontent links that often 403
        // for desktop User-Agents. Prefer maxres; loaders fall back if missing.
        return YouTubeThumbnailHelper.GetVideoThumbnailUrl(videoId);
    }

    private static int ScoreSongCandidate(TimeSpan? duration, string title)
    {
        int score = 0;
        string normalizedTitle = title ?? string.Empty;

        if (duration is null)
        {
            score += 50;
        }
        else if (duration.Value.TotalMinutes is >= 1.5 and <= 8)
        {
            score -= 20;
        }
        else if (duration.Value.TotalMinutes > 20)
        {
            score += 40;
        }

        if (normalizedTitle.Contains("official audio", StringComparison.OrdinalIgnoreCase)
            || normalizedTitle.Contains("lyrics", StringComparison.OrdinalIgnoreCase)
            || normalizedTitle.Contains("topic", StringComparison.OrdinalIgnoreCase))
        {
            score -= 15;
        }

        if (normalizedTitle.Contains("live", StringComparison.OrdinalIgnoreCase)
            || normalizedTitle.Contains("concert", StringComparison.OrdinalIgnoreCase)
            || normalizedTitle.Contains("interview", StringComparison.OrdinalIgnoreCase)
            || normalizedTitle.Contains("trailer", StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }

        return score;
    }

    public async Task<YouTubeStreamSource> ResolveStreamAsync(
        string videoId,
        CancellationToken cancellationToken = default)
    {
        var media = await ResolveMediaAsync(videoId, cancellationToken).ConfigureAwait(false);
        return new YouTubeStreamSource(
            media.VideoId,
            new Uri(media.Stream.Url),
            media.Duration,
            media.Stream.Container.Name,
            media.Stream.Bitrate.BitsPerSecond,
            media.Title);
    }

    public async Task<Song> DownloadAudioAsync(
        string videoId,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var media = await ResolveMediaAsync(videoId, cancellationToken).ConfigureAwait(false);
        string musicDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        Directory.CreateDirectory(musicDirectory);

        string artist = media.Author ?? string.Empty;
        string cleanTitle = Mp3ToSongHelper.CleanSongTitle(media.Title, artist);
        string outputPath = GetUniquePath(musicDirectory, SanitizeFileName(cleanTitle), ".m4a");
        string partialPath = outputPath + ".part";

        try
        {
            await _youtubeClient.Videos.Streams
                .DownloadAsync(media.Stream, partialPath, progress, cancellationToken)
                .ConfigureAwait(false);
            File.Move(partialPath, outputPath);

            byte[]? artwork = media.Video is not null
                ? await TryApplyMetadataAsync(outputPath, media.Video, cancellationToken)
                    .ConfigureAwait(false)
                : null;

            return new Song
            {
                Title = cleanTitle,
                Artist = artist,
                Album = artist,
                Duration = media.Duration,
                FilePath = outputPath,
                VideoId = media.VideoId,
                ThumbnailUrl = media.Video is not null
                    ? GetStableThumbnailUrl(media.Video.Id, media.Video.Thumbnails)
                    : YouTubeThumbnailHelper.GetVideoThumbnailUrl(media.VideoId),
                ChannelTitle = artist,
                Views = media.Video?.Engagement.ViewCount.ToString(CultureInfo.InvariantCulture),
                Year = media.Video?.UploadDate.Year ?? 0,
                ArtworkData = artwork ?? Array.Empty<byte>(),
                CurrentDownloadState = DownloadState.Downloaded
            };
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    public async Task<string> CacheAudioAsync(
        string videoId,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string normalizedId = NormalizeVideoId(videoId);
        string cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoundHaven",
            "Cache",
            "YouTube");
        Directory.CreateDirectory(cacheDirectory);

        string outputPath = Path.Combine(cacheDirectory, $"{normalizedId}.m4a");
        if (IsUsableFile(outputPath))
        {
            return outputPath;
        }

        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsUsableFile(outputPath))
            {
                return outputPath;
            }

            string partialPath = outputPath + ".part";
            TryDelete(partialPath);
            try
            {
                var media = await ResolveMediaAsync(normalizedId, cancellationToken).ConfigureAwait(false);
                await _youtubeClient.Videos.Streams
                    .DownloadAsync(media.Stream, partialPath, progress, cancellationToken)
                    .ConfigureAwait(false);
                File.Move(partialPath, outputPath, true);
                return outputPath;
            }
            catch
            {
                TryDelete(partialPath);
                throw;
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public string NormalizeVideoId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A YouTube video ID or URL is required.", nameof(value));
        }

        string candidate = value.Trim();
        if (IsVideoId(candidate))
        {
            return candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri))
        {
            throw new FormatException("The value is not a valid YouTube video ID or URL.");
        }

        string host = uri.Host.ToLowerInvariant();
        if (host is "youtu.be" or "www.youtu.be")
        {
            candidate = uri.AbsolutePath.Trim('/');
        }
        else if (host.EndsWith("youtube.com", StringComparison.Ordinal))
        {
            candidate = GetQueryValue(uri.Query, "v")
                ?? GetPathVideoId(uri.AbsolutePath)
                ?? string.Empty;
        }

        if (!IsVideoId(candidate))
        {
            throw new FormatException("The YouTube URL does not contain a valid video ID.");
        }

        return candidate;
    }

    public void Dispose()
    {
        _youtubeClient.Dispose();
        _cacheLock.Dispose();
    }

    private async Task<ResolvedMedia> ResolveMediaAsync(
        string videoId,
        CancellationToken cancellationToken)
    {
        string normalizedId = NormalizeVideoId(videoId);

        // Stream manifest is required for playback. Watch-page metadata often fails for
        // Music catalogue / region / age-gated IDs even when audio streams are available.
        Task<StreamManifest> manifestTask = _youtubeClient.Videos.Streams
            .GetManifestAsync(normalizedId, cancellationToken)
            .AsTask();
        Task<Video?> videoTask = TryGetVideoAsync(normalizedId, cancellationToken);

        StreamManifest manifest;
        try
        {
            manifest = await manifestTask.ConfigureAwait(false);
        }
        catch (VideoUnavailableException exception)
        {
            throw new InvalidOperationException(
                $"This YouTube track isn’t available to stream right now ({normalizedId}). "
                + "It may be region-locked, age-restricted, or blocked for anonymous clients.",
                exception);
        }
        catch (VideoUnplayableException exception)
        {
            throw new InvalidOperationException(
                $"This YouTube track can’t be played ({normalizedId}): {exception.Message}",
                exception);
        }

        Video? video = await videoTask.ConfigureAwait(false);

        IStreamInfo stream = SelectCompatibleAudioStream(
            manifest.GetAudioOnlyStreams(),
            candidate => candidate.Container == Container.Mp4,
            candidate => candidate.Bitrate.BitsPerSecond);

        return new ResolvedMedia(
            normalizedId,
            video?.Title ?? normalizedId,
            video?.Author.ChannelTitle,
            video?.Duration ?? TimeSpan.Zero,
            stream,
            video);
    }

    private async Task<Video?> TryGetVideoAsync(string videoId, CancellationToken cancellationToken)
    {
        try
        {
            return await _youtubeClient.Videos.GetAsync(videoId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    internal static T SelectCompatibleAudioStream<T>(
        IEnumerable<T> streams,
        Func<T, bool> isCompatible,
        Func<T, long> getBitrate)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(isCompatible);
        ArgumentNullException.ThrowIfNull(getBitrate);

        return streams
            .Where(isCompatible)
            .OrderByDescending(getBitrate)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "YouTube did not provide a Windows-compatible M4A audio stream for this video.");
    }

    private async Task<byte[]?> TryApplyMetadataAsync(
        string filePath,
        Video video,
        CancellationToken cancellationToken)
    {
        byte[]? artwork = null;
        string? mimeType = null;
        foreach (string thumbnailUrl in YouTubeThumbnailHelper.GetDownloadCandidates(
                     GetStableThumbnailUrl(video.Id, video.Thumbnails),
                     video.Id.Value))
        {
            try
            {
                using HttpResponseMessage response = await _httpClient.GetAsync(
                    thumbnailUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                byte[] imageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (imageBytes.Length == 0
                    || YouTubeThumbnailHelper.LooksLikeMissingYouTubeThumbnail(imageBytes))
                {
                    continue;
                }

                mimeType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                artwork = imageBytes;
                break;
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                // Try the next candidate.
            }
        }

        try
        {
            using TagLib.File tagFile = TagLib.File.Create(filePath);
            tagFile.Tag.Title = Mp3ToSongHelper.CleanSongTitle(
                video.Title,
                video.Author.ChannelTitle);
            tagFile.Tag.Performers = [video.Author.ChannelTitle];
            tagFile.Tag.Album = video.Author.ChannelTitle;
            tagFile.Tag.Year = (uint)video.UploadDate.Year;

            if (artwork is { Length: > 0 })
            {
                tagFile.Tag.Pictures =
                [
                    new TagLib.Picture(new TagLib.ByteVector(artwork))
                    {
                        Type = TagLib.PictureType.FrontCover,
                        Description = "Cover",
                        MimeType = mimeType ?? "image/jpeg"
                    }
                ];
            }

            tagFile.Save();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Metadata is best effort; a valid downloaded audio file is still useful.
        }

        return artwork;
    }

    private static string? GetQueryValue(string query, string key)
    {
        foreach (string part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=', 2);
            if (pair.Length == 2 && string.Equals(
                    Uri.UnescapeDataString(pair[0]),
                    key,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return null;
    }

    private static string? GetPathVideoId(string path)
    {
        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0] is "embed" or "shorts" or "live")
        {
            return segments[1];
        }

        return null;
    }

    private static bool IsVideoId(string value)
    {
        return value.Length == 11
            && value.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        string sanitized = new(value
            .Where(character => !invalidCharacters.Contains(character))
            .ToArray());
        sanitized = sanitized.Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "YouTube audio" : sanitized;
    }

    private static string GetUniquePath(string directory, string baseName, string extension)
    {
        string path = Path.Combine(directory, baseName + extension);
        for (int suffix = 2; File.Exists(path) || File.Exists(path + ".part"); suffix++)
        {
            path = Path.Combine(directory, $"{baseName} ({suffix}){extension}");
        }

        return path;
    }

    private static bool IsUsableFile(string path)
    {
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; the next operation will replace stale partial files.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup; preserve the original operation's exception.
        }
    }

    private sealed record ResolvedMedia(
        string VideoId,
        string Title,
        string? Author,
        TimeSpan Duration,
        IStreamInfo Stream,
        Video? Video);
}
