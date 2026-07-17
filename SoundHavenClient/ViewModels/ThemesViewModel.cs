using Avalonia;
using Avalonia.Media;
using SoundHaven.Data;

namespace SoundHaven.ViewModels
{
    /// <summary>
    /// Drives the app's dynamic accent colour: the primary colour follows the
    /// dominant colour of the current song's artwork. There is no manual theme
    /// picker any more — dynamic theming is always on.
    /// </summary>
    public class ThemesViewModel : ViewModelBase
    {
        private readonly AppDatabase _appDatabase;

        public ThemesViewModel(AppDatabase appDatabase)
        {
            _appDatabase = appDatabase;
            LoadSavedColor();
        }

        public void ApplyDynamicColor(Color color)
        {
            if (Application.Current is null)
            {
                return;
            }

            Application.Current.Resources["PrimaryColor"] = color;
            Application.Current.Resources["PrimaryHueMidBrush"] = new SolidColorBrush(color);
            _appDatabase.SaveThemeColor(color.ToString());
        }

        // Start from the last artwork colour so the accent isn't the default
        // until the first track plays.
        private void LoadSavedColor()
        {
            string savedColorHex = _appDatabase.GetThemeColor();
            if (!string.IsNullOrEmpty(savedColorHex))
            {
                ApplyDynamicColor(Color.Parse(savedColorHex));
            }
        }
    }
}
