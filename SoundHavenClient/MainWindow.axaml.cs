using Avalonia.Controls;
using Avalonia.Input;
using SoundHaven.ViewModels;

namespace SoundHaven
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();
        }

        private void MinimizeButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        }

        private void OnPointerPressedTitleBar(object sender, PointerPressedEventArgs e)
        {
            BeginMoveDrag(e);
        }

        private void CloseButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }
    }
}
