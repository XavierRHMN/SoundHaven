using Avalonia.Controls;
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
}
