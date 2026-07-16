using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SoundHaven.Converters
{
    public class BooleanToOpacityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isChecked && parameter is string opacities)
            {
                string[] opacityArray = opacities.Split(',');
                if (opacityArray.Length == 2
                    && double.TryParse(
                        isChecked ? opacityArray[0] : opacityArray[1],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double opacity))
                {
                    return opacity;
                }
            }
            return 1.0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}


