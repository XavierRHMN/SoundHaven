using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Data.Converters;

namespace SoundHaven.Converters
{
    public class VideoDurationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string durationString)
            {
                var regex = new Regex(@"PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?");
                var match = regex.Match(durationString);

                if (match.Success)
                {
                    int hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
                    int minutes = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
                    int seconds = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;

                    if (hours > 0)
                    {
                        return $"{hours}:{minutes:D2}:{seconds:D2}";
                    }
                    else
                    {
                        return $"{minutes}:{seconds:D2}";
                    }
                }
            }

            return "0:00";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
