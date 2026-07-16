using Avalonia.Controls;
using Avalonia.Interactivity;
using SoundHaven.Helpers;
using SoundHaven.ViewModels;

namespace SoundHaven.Views;

public partial class PlaylistView : UserControl
{
    public PlaylistView()
    {
        InitializeComponent();
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
