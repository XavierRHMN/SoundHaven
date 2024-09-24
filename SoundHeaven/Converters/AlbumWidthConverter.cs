using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SoundHeaven.Converters
{
    public class AlbumCoverWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double scrollViewerWidth)
            {
                // Example: Each album cover takes 20% of the ScrollViewer's width
                return scrollViewerWidth * 0.2;
            }
            return 100.0; // Fallback to default width
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
