using System.Linq;
using Avalonia.Controls;
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
        var addToPlaylist = new MenuItem { Header = "Add to playlist" };

        var playlists = viewModel.Playlists.Where(playlist => playlist.Id > 0).ToList();
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
                    Command = viewModel.AddAlbumToPlaylistCommand,
                    CommandParameter = playlist
                });
            }
        }

        flyout.Items.Add(addToPlaylist);
        flyout.ShowAt(button);
    }
}
