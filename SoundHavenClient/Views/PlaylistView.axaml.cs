using Avalonia.Controls;
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
}
