using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace SoundHaven.Controls;

public sealed class AsyncImageControl : Image, IDisposable
{
    public static readonly StyledProperty<string?> SourceUrlProperty =
        AvaloniaProperty.Register<AsyncImageControl, string?>(nameof(SourceUrl));

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private CancellationTokenSource? _loadCancellation;
    private int _loadGeneration;

    static AsyncImageControl()
    {
        SourceUrlProperty.Changed.AddClassHandler<AsyncImageControl>(
            static (control, change) => control.OnSourceUrlChanged(change.NewValue as string));
    }

    public AsyncImageControl()
    {
        DetachedFromVisualTree += (_, _) => CancelLoad();
    }

    public string? SourceUrl
    {
        get => GetValue(SourceUrlProperty);
        set => SetValue(SourceUrlProperty, value);
    }

    public void Dispose()
    {
        CancelLoad();
        ReplaceSource(null);
        GC.SuppressFinalize(this);
    }

    private void OnSourceUrlChanged(string? url)
    {
        CancelLoad();
        ReplaceSource(null);

        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        _loadCancellation = new CancellationTokenSource();
        int generation = Interlocked.Increment(ref _loadGeneration);
        _ = LoadAsync(url, generation, _loadCancellation.Token);
    }

    private async Task LoadAsync(
        string url,
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await HttpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            // Avalonia's Bitmap decoder needs a seekable stream; network streams are not.
            byte[] imageBytes = await response.Content
                .ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            if (imageBytes.Length == 0)
            {
                return;
            }

            using var memoryStream = new MemoryStream(imageBytes, writable: false);
            var bitmap = new Bitmap(memoryStream);

            if (cancellationToken.IsCancellationRequested
                || generation != Volatile.Read(ref _loadGeneration))
            {
                bitmap.Dispose();
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested
                    || generation != Volatile.Read(ref _loadGeneration))
                {
                    bitmap.Dispose();
                    return;
                }

                ReplaceSource(bitmap);
            });
        }
        catch (OperationCanceledException)
        {
            // The control was recycled or received a newer URL.
        }
        catch (HttpRequestException)
        {
            await ClearSourceOnUiAsync(generation, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            await ClearSourceOnUiAsync(generation, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            await ClearSourceOnUiAsync(generation, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ClearSourceOnUiAsync(
        int generation,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested
            || generation != Volatile.Read(ref _loadGeneration))
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (generation == Volatile.Read(ref _loadGeneration))
            {
                ReplaceSource(null);
            }
        });
    }

    private void CancelLoad()
    {
        Interlocked.Increment(ref _loadGeneration);
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }

    private void ReplaceSource(Bitmap? bitmap)
    {
        if (Source is Bitmap oldBitmap && !ReferenceEquals(oldBitmap, bitmap))
        {
            oldBitmap.Dispose();
        }

        Source = bitmap;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
            + "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("image/*"));
        return client;
    }
}
