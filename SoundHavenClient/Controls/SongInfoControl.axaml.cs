using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SoundHaven.ViewModels;

namespace SoundHaven.Controls
{
    public partial class SongInfoControl : UserControl
    {
        public SongInfoControl()
        {
            InitializeComponent();
        }

        private void OnThumbnailPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel viewModel)
            {
                e.Handled = true;
                viewModel.ToolbarViewModel.ShowPlayerViewCommand.Execute(null);
            }
        }
    }
}
