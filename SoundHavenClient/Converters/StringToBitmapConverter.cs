// Converters/StringToBitmapConverter.cs
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;

namespace SoundHaven.Converters
{
    public class StringToBitmapConverter : IValueConverter
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string url && !string.IsNullOrEmpty(url))
            {
                var bitmapTask = LoadBitmapAsync(url);
                return new TaskCompletionNotifier<Bitmap>(bitmapTask);
            }

            return null;
        }

        private async Task<Bitmap> LoadBitmapAsync(string url)
        {
            try
            {
                byte[]? bytes = await _httpClient.GetByteArrayAsync(url);
                using (var stream = new System.IO.MemoryStream(bytes))
                {
                    return await Task.Run(() => Bitmap.DecodeToWidth(stream, 50)); // Adjust as needed
                }
            }
            catch
            {
                // Return a default image or null in case of error
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }


    // Helper class to support async loading in bindings
    public class TaskCompletionNotifier<T> : System.ComponentModel.INotifyPropertyChanged
    {
        public Task<T> Task { get; private set; }

        public TaskCompletionNotifier(Task<T> task)
        {
            Task = task;
            if (!task.IsCompleted)
            {
                var _ = WatchTaskAsync(task);
            }
        }

        private async Task WatchTaskAsync(Task<T> task)
        {
            try
            {
                await task;
            }
            catch
            {
                // Handle exceptions if needed
            }

            OnPropertyChanged(nameof(Result));
        }

        public T Result
        {
            get
            {
                return Task.Status == TaskStatus.RanToCompletion ? Task.Result : default;
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}
