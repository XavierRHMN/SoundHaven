using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace SoundHaven.Controls
{
    public class AsyncImageControl : Image
    {
        public static readonly StyledProperty<string> SourceUrlProperty =
            AvaloniaProperty.Register<AsyncImageControl, string>(nameof(SourceUrl));

        private static readonly HttpClient _httpClient = new HttpClient();

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
            await Dispatcher.UIThread.InvokeAsync(() => Source = new Bitmap(@"C:\Users\mdsha\RiderProjects\SoundHaven\SoundHavenClient\Assets\Covers\MissingAlbum.png"));

            string? url = e.NewValue as string;
            if (!string.IsNullOrEmpty(url))
            {
                try
                {
                    var response = await _httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var stream = await response.Content.ReadAsStreamAsync();

                        // Ensure bitmap decoding happens on the UI thread
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            try
                            {
                                var bitmap = new Bitmap(stream);
                                Source = bitmap;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error decoding bitmap from {url}: {ex.Message}");
                            }
                        });
                    }
                    else
                    {
                        Debug.WriteLine($"Failed to load image from {url}: {response.StatusCode}");
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
