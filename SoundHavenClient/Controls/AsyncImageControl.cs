using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace SoundHaven.Controls;

public sealed class AsyncImageControl : Image, IDisposable
{
    public static readonly StyledProperty<string?> SourceUrlProperty =
        AvaloniaProperty.Register<AsyncImageControl, string?>(nameof(SourceUrl));

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private CancellationTokenSource? _loadCancellation;

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
        _ = LoadAsync(url, _loadCancellation.Token);
    }

    private async Task LoadAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await HttpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var bitmap = new Bitmap(stream);

            if (cancellationToken.IsCancellationRequested)
            {
                bitmap.Dispose();
                return;
            }

            ReplaceSource(bitmap);
        }
        catch (OperationCanceledException)
        {
            // The control was recycled or received a newer URL.
        }
        catch (HttpRequestException)
        {
            ReplaceSource(null);
        }
        catch (ArgumentException)
        {
            ReplaceSource(null);
        }
    }

    private void CancelLoad()
    {
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
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("SoundHaven", "1.0"));
        return client;
    }
}
