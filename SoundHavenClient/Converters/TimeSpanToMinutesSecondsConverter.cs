using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SoundHaven.Converters
{
    public class TimeSpanToMinutesSecondsConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double seconds)
            {
                var time = TimeSpan.FromSeconds(Math.Floor(seconds));
                return time.TotalHours >= 1
                    ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                    : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
            }

            if (value is TimeSpan timeSpan)
            {
                return timeSpan.TotalHours >= 1
                    ? timeSpan.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                    : timeSpan.ToString(@"m\:ss", CultureInfo.InvariantCulture);
            }
            return "00:00";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
