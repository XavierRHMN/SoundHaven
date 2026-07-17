using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;

namespace SoundHaven.Views;

/// <summary>
/// Compact always-on-top player (TIDAL-style): art, track info, seek, transport.
/// Opened from the player bar's mini-player button; closing restores MainWindow.
/// </summary>
public partial class MiniPlayerWindow : Window
{
    public MiniPlayerWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, System.EventArgs e)
    {
        // Park in the top-right corner of the working area.
        Screen? screen = Screens.Primary ?? Screens.ScreenFromWindow(this);
        if (screen is null)
        {
            return;
        }

        PixelRect area = screen.WorkingArea;
        double scale = RenderScaling;
        int width = (int)(Width * scale);
        Position = new PixelPoint(area.Right - width - 24, area.Y + 24);
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
