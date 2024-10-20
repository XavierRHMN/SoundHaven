using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using SoundHaven.Commands;
using SoundHaven.Data;
using SoundHaven.Helpers;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace SoundHaven.ViewModels
{
    public class ThemesViewModel : ViewModelBase
    {
        private readonly AppDatabase _appDatabase;
        public RelayCommand<Color> ChangeThemeCommand { get; }
        
        private bool _isDynamicThemeSelected;
        public bool IsDynamicThemeSelected
        {
            get => _isDynamicThemeSelected;
            set => SetProperty(ref _isDynamicThemeSelected, value);
        }
        
        public List<Color> ThemeColors { get; } = new List<Color>
        {
            // Reds
            Color.Parse("#FF1744"), // Red (A400)
            Color.Parse("#FF5252"), // Red (A200)
            Color.Parse("#FF4081"), // Pink (A200)
            Color.Parse("#F50057"), // Pink (A400)
            Color.Parse("#D81B60"), // Pink (600)

            // Purples
            Color.Parse("#E040FB"), // Purple (A200)
            Color.Parse("#AA00FF"), // Purple (A700)
            Color.Parse("#8E24AA"), // Purple (600)
            Color.Parse("#6200EA"), // Deep Purple (A700)

            // Blues
            Color.Parse("#3D5AFE"), // Indigo (A400)
            Color.Parse("#2979FF"), // Blue (A400)
            Color.Parse("#1E88E5"), // Blue (600)
            Color.Parse("#00B0FF"), // Light Blue (A400)
            Color.Parse("#039BE5"), // Light Blue (600)

            // Cyans and Teals
            Color.Parse("#00E5FF"), // Cyan (A400)
            Color.Parse("#00ACC1"), // Cyan (600)
            Color.Parse("#1DE9B6"), // Teal (A400)

            // Greens
            Color.Parse("#00E676"), // Green (A400)
            Color.Parse("#43A047"), // Green (600)
            Color.Parse("#76FF03"), // Light Green (A400)
            Color.Parse("#7CB342"), // Light Green (600)

            // Yellows and Ambers
            Color.Parse("#C0CA33"), // Lime (600)
            Color.Parse("#FDD835"), // Yellow (600)
            Color.Parse("#FFC400"), // Amber (A400)
            Color.Parse("#FFB300"), // Amber (600)

            // Oranges
            Color.Parse("#FF9100"), // Orange (A400)
            Color.Parse("#FF3D00"), // Deep Orange (A400)
            Color.Parse("#F4511E"), // Deep Orange (600)

            // Browns and Greys
            Color.Parse("#795548"), // Brown (500)
            Color.Parse("#6D4C41"), // Brown (600)
            Color.Parse("#757575"), // Grey (600)
            Color.Parse("#546E7A"), // Blue Grey (600)

            Color.Parse("#FFFFFF"), // White
        };

        public ThemesViewModel(AppDatabase appDatabase)
        {
            _appDatabase = appDatabase;
            ChangeThemeCommand = new RelayCommand<Color>(ChangeTheme);
            ChangeTheme(ThemeColors.LastOrDefault());
            LoadSavedTheme();
        }


        public void ChangeTheme(Color newColor)
        {
            if (Application.Current != null)
            {
                Application.Current.Resources["PrimaryColor"] = newColor;
                Application.Current.Resources["PrimaryHueMidBrush"] = new SolidColorBrush(newColor);
                
                IsDynamicThemeSelected = newColor == ThemeColors[^1];
                
                // Save the new theme color to the database
                _appDatabase.SaveThemeColor(newColor.ToString());

                // Optionally, you can log the change
                System.Console.WriteLine($"Theme changed to: {newColor}");
            }
        }

        private void LoadSavedTheme()
        {
            string savedColorHex = _appDatabase.GetThemeColor();
            if (!string.IsNullOrEmpty(savedColorHex))
            {
                var savedColor = Color.Parse(savedColorHex);
                ChangeTheme(savedColor);
            }
        }
    }
}
