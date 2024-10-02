using Avalonia.Data.Converters;
using System;
using System.Globalization;
using System.IO;

namespace SoundHaven.Converters
{
    public class FilePathShortenerConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string filePath)
            {
                return filePath;
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
