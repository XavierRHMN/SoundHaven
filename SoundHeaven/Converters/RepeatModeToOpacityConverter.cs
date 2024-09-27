using Avalonia.Data.Converters;
using SoundHeaven.ViewModels;
using System;
using System.Globalization;

namespace SoundHeaven.Converters
{
    public class RepeatModeToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is RepeatMode mode)
            {
                return mode switch
                {
                    RepeatMode.Off => 0.5,
                    RepeatMode.All => 1.0,
                    RepeatMode.One => 1.0,
                    _ => 0.5
                };
            }
            return 0.5;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
