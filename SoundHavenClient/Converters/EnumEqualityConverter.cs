using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SoundHaven.Converters
{
    public class EnumEqualityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value?.Equals(parameter) ?? false;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
