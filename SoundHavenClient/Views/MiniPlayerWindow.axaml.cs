using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SoundHaven.Views;

/// <summary>
/// Compact always-on-top player: the album-art card with a hover-reveal overlay
/// (close, track info, seek, transport). Opened from the player bar's
/// mini-player button; closing restores MainWindow. Starts centered on screen.
/// </summary>
public partial class MiniPlayerWindow : Window
{
    public MiniPlayerWindow()
    {
        InitializeComponent();
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Buttons and the slider handle their own presses; anything that reaches
        // the root drags the window (it has no title bar).
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Close();
    }
}
