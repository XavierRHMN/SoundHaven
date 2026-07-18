using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SoundHaven.Helpers;
using SoundHaven.Models;
using SoundHaven.ViewModels;

namespace SoundHaven.Views;

public partial class PlaylistView : UserControl
{
    public PlaylistView()
    {
        InitializeComponent();

        // Tunnel so the row context menu wins over DataGrid's internal selection
        // handling (selection triggers playback via OnSelectionChanged).
        TrackGrid.AddHandler(
            PointerPressedEvent,
            OnTrackGridPointerPressed,
            RoutingStrategies.Tunnel);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (DataContext is PlaylistViewModel { IsEditMode: false } viewModel
            && sender is DataGrid { SelectedItem: PlaylistTrackRow row }
            && viewModel.PlaySongCommand.CanExecute(row.Song))
        {
            viewModel.PlaySongCommand.Execute(row.Song);
        }
    }

    private void OnTrackGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            return;
        }

        if (DataContext is not PlaylistViewModel { IsEditMode: false } viewModel)
        {
            return;
        }

        if (e.Source is not Control source
            || source.FindAncestorOfType<DataGridRow>(includeSelf: true)
                is not { DataContext: PlaylistTrackRow row } gridRow)
        {
            return;
        }

        e.Handled = true;
        ShowSongMenu(gridRow, viewModel, row.Song, showAtPointer: true);
    }

    private void OnRowActionsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Keep ⋯ presses from selecting the row (selection auto-plays).
        e.Handled = true;
    }

    private void OnRowMoreButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PlaylistViewModel { IsEditMode: false } viewModel
            || sender is not Button { DataContext: PlaylistTrackRow row } button)
        {
            return;
        }

        e.Handled = true;
        ShowSongMenu(button, viewModel, row.Song, showAtPointer: false);
    }

    private static void ShowSongMenu(
        Control anchor,
        PlaylistViewModel viewModel,
        Song song,
        bool showAtPointer)
    {
        viewModel.SetMenuSong(song);

        var flyout = DarkMenuFlyout.Create(
            showAtPointer ? PlacementMode.Pointer : PlacementMode.BottomEdgeAlignedLeft);

        flyout.Items.Add(new MenuItem
        {
            Header = "Play next",
            Command = viewModel.PlaySongNextCommand,
            CommandParameter = song
        });

        flyout.Items.Add(new MenuItem
        {
            Header = "Add to queue",
            Command = viewModel.AddToQueueCommand,
            CommandParameter = song
        });

        var addToPlaylist = new MenuItem { Header = "Add to playlist" };
        var otherPlaylists = viewModel.AllPlaylists
            .Where(playlist => playlist.Id > 0
                && !playlist.IsDownloads
                && !ReferenceEquals(playlist, viewModel.DisplayedPlaylist))
            .ToList();
        if (otherPlaylists.Count == 0)
        {
            addToPlaylist.Items.Add(new MenuItem
            {
                Header = "No other playlists",
                IsEnabled = false
            });
        }
        else
        {
            foreach (Playlist playlist in otherPlaylists)
            {
                addToPlaylist.Items.Add(new MenuItem
                {
                    Header = playlist.Name,
                    Command = viewModel.AddMenuSongToPlaylistCommand,
                    CommandParameter = playlist
                });
            }
        }

        flyout.Items.Add(addToPlaylist);

        if (!string.IsNullOrWhiteSpace(song.FilePath))
        {
            flyout.Items.Add(new MenuItem
            {
                Header = "Show in folder",
                Command = viewModel.OpenSongFolderCommand,
                CommandParameter = song
            });
        }

        // Downloaded Songs derives its membership from what's on disk, so the row's
        // removal action is "undownload" rather than plain membership removal
        // (which the startup reconcile would just re-add).
        if (viewModel.IsDownloadsPlaylist)
        {
            flyout.Items.Add(new MenuItem
            {
                Header = "Remove download",
                Command = viewModel.RemoveDownloadCommand,
                CommandParameter = song
            });
        }
        else
        {
            flyout.Items.Add(new MenuItem
            {
                Header = "Remove from playlist",
                Command = viewModel.RemoveSongFromPlaylistCommand,
                CommandParameter = song
            });
        }

        flyout.ShowAt(anchor, showAtPointer);
    }

    private void OnMoreButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PlaylistViewModel viewModel || viewModel.IsEditMode)
        {
            return;
        }

        if (sender is not Button button)
        {
            return;
        }

        var flyout = DarkMenuFlyout.Create(PlacementMode.TopEdgeAlignedRight);

        flyout.Items.Add(new MenuItem
        {
            Header = "Play next",
            Command = viewModel.PlayPlaylistNextCommand
        });

        // Downloaded Songs membership mirrors the disk; manual add/remove would be
        // undone by the startup reconcile, so those entries are hidden there.
        if (!viewModel.IsDownloadsPlaylist)
        {
            flyout.Items.Add(new MenuItem
            {
                Header = "Add from storage",
                Command = viewModel.AddSongCommand
            });
            flyout.Items.Add(new MenuItem
            {
                Header = "Remove songs",
                Command = viewModel.EnterRemoveSongsCommand
            });
        }

        flyout.ShowAt(button);
    }
}
