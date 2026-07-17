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
        ShowSongMenu(gridRow, viewModel, row.Song);
    }

    private static void ShowSongMenu(Control anchor, PlaylistViewModel viewModel, Song song)
    {
        viewModel.SetMenuSong(song);

        var flyout = DarkMenuFlyout.Create(PlacementMode.Pointer);

        flyout.Items.Add(new MenuItem
        {
            Header = "Play next",
            Command = viewModel.PlaySongNextCommand,
            CommandParameter = song
        });

        var addToPlaylist = new MenuItem { Header = "Add to playlist" };
        var otherPlaylists = viewModel.AllPlaylists
            .Where(playlist => playlist.Id > 0
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

        flyout.Items.Add(new MenuItem
        {
            Header = "Remove from playlist",
            Command = viewModel.RemoveSongFromPlaylistCommand,
            CommandParameter = song
        });

        flyout.ShowAt(anchor, showAtPointer: true);
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

        flyout.ShowAt(button);
    }
}
