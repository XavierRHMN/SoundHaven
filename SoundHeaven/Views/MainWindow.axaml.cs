using Avalonia.Controls;
using SoundHeaven.ViewModels;

namespace SoundHeaven.Views
{
    public partial class MainWindow : Window {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();
        }
    }
}
