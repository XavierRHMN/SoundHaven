using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using SoundHaven.Commands;
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

        // Only recommendation cards offer "Not interested".
        if (viewModel.IsRecommendedSong(song))
        {
            flyout.Items.Add(new MenuItem
            {
                Header = "Not interested",
                Command = viewModel.DislikeSongCommand,
                CommandParameter = song
            });
        }

        flyout.ShowAt(anchor, showAtPointer);
    }

    private void OnSearchRowSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is HomeViewModel { Search: { } search }
            && sender is ListBox { SelectedItem: SearchResultRow row }
            && search.PlaySongCommand.CanExecute(row.Song))
        {
            search.PlaySongCommand.Execute(row.Song);
        }
    }

    private void OnSearchActionsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Keep row action clicks from selecting the row (which auto-plays).
        e.Handled = true;
    }

    private void OnSearchOverflowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button
            || button.DataContext is not SearchResultRow row
            || DataContext is not HomeViewModel { Search: { } search })
        {
            return;
        }

        Song song = row.Song;
        search.SetMenuSong(song);

        var flyout = DarkMenuFlyout.Create(PlacementMode.BottomEdgeAlignedLeft);
        flyout.Items.Add(new MenuItem
        {
            Header = "Play now",
            Command = search.PlaySongCommand,
            CommandParameter = song
        });
        flyout.Items.Add(new MenuItem
        {
            Header = "Play next",
            Command = search.PlayNextCommand,
            CommandParameter = song
        });
        flyout.Items.Add(new MenuItem
        {
            Header = "Add to Up Next",
            Command = search.AddToUpNextCommand,
            CommandParameter = song
        });

        var addToPlaylist = new MenuItem { Header = "Add to playlist" };
        foreach (Playlist playlist in search.Playlists)
        {
            addToPlaylist.Items.Add(new MenuItem
            {
                Header = playlist.Name,
                Command = search.AddToPlaylistCommand,
                CommandParameter = playlist
            });
        }

        if (search.Playlists.Count > 0)
        {
            addToPlaylist.Items.Add(new Separator());
        }

        addToPlaylist.Items.Add(new MenuItem
        {
            Header = "Create one",
            Command = search.CreatePlaylistAndAddSongCommand,
            Icon = new PathIcon
            {
                Data = StreamGeometry.Parse("M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z"),
                Width = 16,
                Height = 16,
                Foreground = Brushes.White
            }
        });

        flyout.Items.Add(addToPlaylist);
        flyout.ShowAt(button);
        e.Handled = true;
    }

    private async void OnConnectLastFmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel viewModel
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        e.Handled = true;
        var dialog = new LastFmSignInWindow(viewModel.LastFm);
        await dialog.ShowDialog(owner);
    }

    private void OnLastFmAccountClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not HomeViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        var flyout = DarkMenuFlyout.Create(PlacementMode.BottomEdgeAlignedRight);
        flyout.Items.Add(new MenuItem
        {
            Header = "Disconnect Last.fm",
            Command = new RelayCommand(viewModel.LastFm.SignOut)
        });
        flyout.ShowAt(button);
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
