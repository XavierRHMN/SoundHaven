using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using SoundHaven.Models;
using SoundHaven.ViewModels;

namespace SoundHaven.Views;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (DataContext is SearchViewModel viewModel
            && sender is ListBox { SelectedItem: Song song }
            && viewModel.PlaySongCommand.CanExecute(song))
        {
            viewModel.PlaySongCommand.Execute(song);
        }
    }

    private void OnActionsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Keep row action clicks from selecting the item (which would auto-play).
        e.Handled = true;
    }

    private void OnOverflowButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button
            || button.DataContext is not Song song
            || DataContext is not SearchViewModel viewModel)
        {
            return;
        }

        viewModel.SetMenuSong(song);

        var flyout = new MenuFlyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft
        };

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
        flyout.Items.Add(new MenuItem
        {
            Header = "Add to Up Next",
            Command = viewModel.AddToUpNextCommand,
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
                Height = 16
            }
        });

        flyout.Items.Add(addToPlaylist);
        flyout.ShowAt(button);
        e.Handled = true;
    }
}
