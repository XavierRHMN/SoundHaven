using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SoundHaven.Models;
using SoundHaven.ViewModels;

namespace SoundHaven.Views;

public partial class LikedAlbumsView : UserControl
{
    public LikedAlbumsView()
    {
        InitializeComponent();
    }

    private void OnAlbumCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Clicks on the hover Play/heart buttons run their own commands.
        if (e.Source is Control source
            && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        if (sender is not Control control
            || control.DataContext is not Song album
            || DataContext is not LikedAlbumsViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        if (viewModel.OpenAlbumCommand.CanExecute(album))
        {
            viewModel.OpenAlbumCommand.Execute(album);
        }
    }
}
