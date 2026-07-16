using Avalonia.Controls;
using Avalonia.Input;
using SoundHaven.Helpers;
using SoundHaven.Models;
using SoundHaven.ViewModels;

namespace SoundHaven.Controls;

public partial class ToolbarControl : UserControl
{
    public ToolbarControl()
    {
        InitializeComponent();
    }

    private void OnPlaylistPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            return;
        }

        if (sender is not Control control
            || control.DataContext is not Playlist playlist
            || DataContext is not ToolbarViewModel viewModel)
        {
            return;
        }

        // Stop ListBox from selecting (which navigates and feels laggy).
        e.Handled = true;

        var flyout = DarkMenuFlyout.Create(PlacementMode.Right);
        flyout.HorizontalOffset = 8;
        flyout.Items.Add(new MenuItem
        {
            Header = "Play now",
            Command = viewModel.PlayNowCommand,
            CommandParameter = playlist
        });
        flyout.Items.Add(new MenuItem
        {
            Header = "Shuffle",
            Command = viewModel.ShufflePlaylistCommand,
            CommandParameter = playlist
        });
        flyout.Items.Add(new MenuItem
        {
            Header = "Play next",
            Command = viewModel.PlayNextCommand,
            CommandParameter = playlist
        });
        flyout.Items.Add(new Separator());
        flyout.Items.Add(new MenuItem
        {
            Header = "Edit playlist",
            Command = viewModel.EditPlaylistCommand,
            CommandParameter = playlist
        });
        flyout.Items.Add(new MenuItem
        {
            Header = "Delete playlist",
            Command = viewModel.DeletePlaylistCommand,
            CommandParameter = playlist
        });

        flyout.ShowAt(control);
    }
}
