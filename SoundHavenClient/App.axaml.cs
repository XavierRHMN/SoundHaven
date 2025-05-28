using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using SoundHaven.Services;
using SoundHaven.ViewModels;
using SoundHaven.Views;

namespace SoundHaven
{
    public partial class App : Application
    {
        private AudioService _audioService;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

     public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Line below is needed to remove Avalonia data validation.
                // Without this line you will get duplicate validations from both Avalonia and CT
                BindingPlugins.DataValidators.RemoveAt(0);

                _audioService = new AudioService();
                var mainViewModel = new MainWindowViewModel(_audioService);

                desktop.MainWindow = new MainWindow
                {
                    DataContext = mainViewModel
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
