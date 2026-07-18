using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SoundHaven.Helpers;
using SoundHaven.Models;
using SoundHaven.ViewModels;

namespace SoundHaven.Views;

public partial class AlbumView : UserControl
{
    public AlbumView()
    {
        InitializeComponent();
    }

    // Selecting a row (single click) plays the album from that track, matching the
    // playlist page's behavior.
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (DataContext is AlbumViewModel viewModel
            && sender is DataGrid { SelectedItem: PlaylistTrackRow row }
            && viewModel.PlayTrackCommand.CanExecute(row.Song))
        {
            viewModel.PlayTrackCommand.Execute(row.Song);
        }
    }

    private void OnMoreButtonClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not AlbumViewModel viewModel || sender is not Button button)
        {
            return;
        }

        eventArgs.Handled = true;

        var flyout = DarkMenuFlyout.Create(PlacementMode.BottomEdgeAlignedLeft);
        flyout.Items.Add(BuildAddToPlaylistMenu(viewModel, viewModel.AddAlbumToPlaylistCommand));
        flyout.ShowAt(button);
    }

    // Keep row-action presses (on the empty gaps between buttons) from selecting the
    // row, which would auto-play the track.
    private void OnRowActionsPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
        => eventArgs.Handled = true;

    private void OnRowMoreButtonClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not AlbumViewModel viewModel
            || sender is not Button { DataContext: PlaylistTrackRow row } button)
        {
            return;
        }

        eventArgs.Handled = true;
        viewModel.SetMenuSong(row.Song);

        var flyout = DarkMenuFlyout.Create(PlacementMode.BottomEdgeAlignedLeft);
        flyout.Items.Add(BuildAddToPlaylistMenu(viewModel, viewModel.AddSongToPlaylistCommand));

        if (!string.IsNullOrWhiteSpace(row.Song.FilePath))
        {
            flyout.Items.Add(new MenuItem
            {
                Header = "Show in folder",
                Command = viewModel.OpenSongFolderCommand,
                CommandParameter = row.Song
            });
        }

        flyout.ShowAt(button);
    }

    private static MenuItem BuildAddToPlaylistMenu(
        AlbumViewModel viewModel,
        System.Windows.Input.ICommand addCommand)
    {
        var addToPlaylist = new MenuItem { Header = "Add to playlist" };

        var playlists = viewModel.Playlists
            .Where(playlist => playlist.Id > 0 && !playlist.IsDownloads)
            .ToList();
        if (playlists.Count == 0)
        {
            addToPlaylist.Items.Add(new MenuItem { Header = "No playlists yet", IsEnabled = false });
        }
        else
        {
            foreach (Playlist playlist in playlists)
            {
                addToPlaylist.Items.Add(new MenuItem
                {
                    Header = playlist.Name,
                    Command = addCommand,
                    CommandParameter = playlist
                });
            }
        }

        return addToPlaylist;
    }
}
