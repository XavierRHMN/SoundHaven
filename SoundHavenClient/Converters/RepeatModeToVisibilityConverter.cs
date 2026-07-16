using System;
using System.Globalization;
using Avalonia.Data.Converters;
using SoundHaven.ViewModels;

namespace SoundHaven.Converters
{
    public class RepeatModeToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is RepeatMode mode && parameter is string param)
            {
                bool isVisible = param.ToLowerInvariant() switch
                {
                    "general" => mode == RepeatMode.All || mode == RepeatMode.Off,
                    "one" => mode == RepeatMode.One,
                    _ => false
                };
                return isVisible;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
