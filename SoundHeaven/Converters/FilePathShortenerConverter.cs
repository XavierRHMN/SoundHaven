using Avalonia.Data.Converters;
using System;
using System.Globalization;
using System.IO;

namespace SoundHeaven.Converters
{
    public class FilePathShortenerConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string filePath)
            {
                string fileName = Path.GetFileName(filePath);
                return $"SoundHeaven/Tracks/{fileName}";
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
