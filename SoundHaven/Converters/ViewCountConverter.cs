using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SoundHaven.Converters
{
    public class ViewCountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ulong viewCount)
            {
                if (viewCount >= 1_000_000_000)
                {
                    return $"{viewCount / 1_000_000_000.0:F1}B";
                }
                else if (viewCount >= 1_000_000)
                {
                    return $"{viewCount / 1_000_000.0:F1}M";
                }
                else if (viewCount >= 1_000)
                {
                    return $"{viewCount / 1_000.0:F1}K";
                }
                else
                {
                    return viewCount.ToString();
                }
            }

            return "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
