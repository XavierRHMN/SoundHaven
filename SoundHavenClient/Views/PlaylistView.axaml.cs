using Avalonia.Controls;
using SoundHaven.Models;
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
            && sender is DataGrid { SelectedItem: Song song }
            && viewModel.PlaySongCommand.CanExecute(song))
        {
            viewModel.PlaySongCommand.Execute(song);
        }
    }
}
