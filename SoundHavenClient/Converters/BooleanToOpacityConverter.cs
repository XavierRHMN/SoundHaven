using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SoundHaven.Converters
{
    public class BooleanToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isChecked && parameter is string opacities)
            {
                string[]? opacityArray = opacities.Split(',');
                return double.Parse(isChecked ? opacityArray[0] : opacityArray[1]);
            }
            return 1.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}


