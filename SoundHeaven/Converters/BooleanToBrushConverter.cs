using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace SoundHeaven.Converters
{
// BooleanToBrushConverter.cs
    public class BooleanToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isChecked && parameter is string colors)
            {
                var colorArray = colors.Split(',');
                return new SolidColorBrush(Color.Parse(isChecked ? colorArray[0] : colorArray[1]));
            }
            return new SolidColorBrush(Colors.White);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
