using Avalonia.Controls;
using SoundHaven.Models;
using SoundHaven.ViewModels;

namespace SoundHaven.Views;

public partial class PlayerView : UserControl
{
    public PlayerView()
    {
        InitializeComponent();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (DataContext is PlayerViewModel viewModel
            && sender is ListBox { SelectedItem: Song song }
            && !ReferenceEquals(song, viewModel.PlayerViewSong)
            && viewModel.PlaySongCommand.CanExecute(song))
        {
            viewModel.PlaySongCommand.Execute(song);
        }
    }
}
