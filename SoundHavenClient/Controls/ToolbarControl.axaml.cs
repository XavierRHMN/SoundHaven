using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SoundHaven.Commands;
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

    private void OnSortButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not ToolbarViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        var flyout = DarkMenuFlyout.Create(PlacementMode.BottomEdgeAlignedRight);
        flyout.Items.Add(new MenuItem
        {
            Header = "Sort",
            IsEnabled = false
        });
        AddSortItem(flyout, viewModel, "Created date", PlaylistSortMode.CreatedDate);
        AddSortItem(flyout, viewModel, "Updated date", PlaylistSortMode.UpdatedDate);
        AddSortItem(flyout, viewModel, "Alphabetical", PlaylistSortMode.Alphabetical);
        flyout.ShowAt(button);
    }

    private static void AddSortItem(
        MenuFlyout flyout,
        ToolbarViewModel viewModel,
        string label,
        PlaylistSortMode mode)
    {
        bool isActive = viewModel.PlaylistSortMode == mode;
        string header = isActive
            ? $"{label}   {(viewModel.PlaylistSortDescending ? "↓" : "↑")}"
            : label;
        flyout.Items.Add(new MenuItem
        {
            Header = header,
            Command = new RelayCommand(() => viewModel.SortPlaylistsBy(mode))
        });
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
        ShowPlaylistMenu(control, viewModel, playlist, showAtPointer: true);
    }

    private void OnOverflowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Keep overflow clicks from selecting/navigating the playlist row.
        e.Handled = true;
    }

    private void OnPlaylistOverflowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button
            || button.DataContext is not Playlist playlist
            || DataContext is not ToolbarViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        ShowPlaylistMenu(button, viewModel, playlist, showAtPointer: false);
    }

    private static void ShowPlaylistMenu(
        Control anchor,
        ToolbarViewModel viewModel,
        Playlist playlist,
        bool showAtPointer)
    {
        var flyout = DarkMenuFlyout.Create(
            showAtPointer ? PlacementMode.Pointer : PlacementMode.Right);
        if (!showAtPointer)
        {
            flyout.HorizontalOffset = 8;
        }
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

        // The Liked / Downloaded system playlists can't be edited or deleted.
        if (!playlist.IsSystemPlaylist)
        {
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
        }

        flyout.ShowAt(anchor, showAtPointer);
    }
}
