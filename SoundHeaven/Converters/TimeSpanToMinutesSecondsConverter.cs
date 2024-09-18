using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SoundHeaven.Converters
{
    public class TimeSpanToMinutesSecondsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TimeSpan timeSpan)
            {
                return $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
            }
            return "00:00";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
