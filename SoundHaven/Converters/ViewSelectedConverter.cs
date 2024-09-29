using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SoundHaven.Converters
{
    public class ViewSelectedConverter : IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Count == 2 && values[0] is string currentView && values[1] is string expectedView)
            {
                var primaryColorBrush = new SolidColorBrush((Color)Application.Current.Resources["PrimaryColor"]);
                var whiteBrush = Brushes.White;

                return currentView == expectedView
                    ? primaryColorBrush
                    : whiteBrush;
            }
            return new SolidColorBrush((Color)Application.Current.Resources["PrimaryColor"]);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
