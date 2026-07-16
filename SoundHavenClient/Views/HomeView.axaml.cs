using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SoundHaven.ViewModels;

namespace SoundHaven.Views
{
    public partial class HomeView : UserControl
    {
        public HomeView()
        {
            InitializeComponent();
        }

        private async void SubmitDetails_OnClick(object? sender, RoutedEventArgs e)
        {
            await SubmitDetailsAsync();
        }

        private async void PasswordBox_OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            await SubmitDetailsAsync();
        }

        private async Task SubmitDetailsAsync()
        {
            if (DataContext is not HomeViewModel viewModel)
            {
                return;
            }

            string password = PasswordBox.Text ?? string.Empty;
            PasswordBox.Text = string.Empty;

            await viewModel.SubmitDetailsAsync(password);
        }
    }
}
