using Avalonia.Controls;
using Avalonia.Controls.Selection;
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
}
