using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using SoundHaven.Helpers;
using SoundHaven.Models;
using SoundHaven.ViewModels;

namespace SoundHaven.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private void OnActionsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnSongDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control control
            || control.DataContext is not Song song
            || DataContext is not HomeViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        if (viewModel.PlaySongCommand.CanExecute(song))
        {
            viewModel.PlaySongCommand.Execute(song);
        }
    }

    private void OnSongOverflowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button
            || button.DataContext is not Song song
            || DataContext is not HomeViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        ShowSongMenu(button, viewModel, song, showAtPointer: false);
    }

    private void OnSongCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            return;
        }

        if (sender is not Control control
            || control.DataContext is not Song song
            || DataContext is not HomeViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        ShowSongMenu(control, viewModel, song, showAtPointer: true);
    }

    private static void ShowSongMenu(
        Control anchor,
        HomeViewModel viewModel,
        Song song,
        bool showAtPointer)
    {
        viewModel.SetMenuSong(song);
        var flyout = DarkMenuFlyout.Create(
            showAtPointer ? PlacementMode.Pointer : PlacementMode.BottomEdgeAlignedLeft);
        flyout.Items.Add(new MenuItem
        {
            Header = "Play now",
            Command = viewModel.PlaySongCommand,
            CommandParameter = song
        });
        flyout.Items.Add(new MenuItem
        {
            Header = "Play next",
            Command = viewModel.PlayNextCommand,
            CommandParameter = song
        });

        var addToPlaylist = new MenuItem { Header = "Add to playlist" };
        foreach (Playlist playlist in viewModel.Playlists)
        {
            addToPlaylist.Items.Add(new MenuItem
            {
                Header = playlist.Name,
                Command = viewModel.AddToPlaylistCommand,
                CommandParameter = playlist
            });
        }

        if (viewModel.Playlists.Count > 0)
        {
            addToPlaylist.Items.Add(new Separator());
        }

        addToPlaylist.Items.Add(new MenuItem
        {
            Header = "Create one",
            Command = viewModel.CreatePlaylistAndAddSongCommand,
            Icon = new PathIcon
            {
                Data = StreamGeometry.Parse("M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z"),
                Width = 16,
                Height = 16,
                Foreground = Brushes.White
            }
        });

        flyout.Items.Add(addToPlaylist);
        flyout.ShowAt(anchor, showAtPointer);
    }

    private void OnPlaylistCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (sender is not Control control
            || control.DataContext is not Playlist playlist
            || DataContext is not HomeViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        if (viewModel.OpenPlaylistCommand.CanExecute(playlist))
        {
            viewModel.OpenPlaylistCommand.Execute(playlist);
        }
    }
}
