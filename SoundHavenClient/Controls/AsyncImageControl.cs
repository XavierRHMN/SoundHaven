using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Platform;

namespace SoundHaven.Controls
{
    public class AsyncImageControl : Image
    {
        public static readonly StyledProperty<string> SourceUrlProperty =
            AvaloniaProperty.Register<AsyncImageControl, string>(nameof(SourceUrl));

        private static readonly HttpClient HttpClient = new();
        private static readonly string FallbackImagePath = "avares://SoundHavenClient/Assets/Covers/MissingAlbum.png";

        public string SourceUrl
        {
            get => GetValue(SourceUrlProperty);
            set => SetValue(SourceUrlProperty, value);
        }

        static AsyncImageControl()
        {
            SourceUrlProperty.Changed.AddClassHandler<AsyncImageControl>((x, e) => x.OnSourceUrlChanged(e));
        }

        private async void OnSourceUrlChanged(AvaloniaPropertyChangedEventArgs e)
        {
            // Set fallback image first
            await Dispatcher.UIThread.InvokeAsync(() => 
            {
                var uri = new Uri(FallbackImagePath);
                var assetLoader = AssetLoader.Open(uri);
                Source = new Bitmap(assetLoader);
            });

            string? url = e.NewValue as string;
            if (!string.IsNullOrEmpty(url))
            {
                try
                {
                    var response = await HttpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        // Ensure bitmap decoding happens on the UI thread
                        var stream = await response.Content.ReadAsStreamAsync();
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var bitmap = new Bitmap(stream);
                            Source = bitmap;
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading image from {url}: {ex.Message}");
                }
            }
        }
    }
}