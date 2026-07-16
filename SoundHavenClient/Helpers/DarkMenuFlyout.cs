using Avalonia.Controls;

namespace SoundHaven.Helpers;

/// <summary>
/// Builds MenuFlyouts that pick up the shared dark rounded menu styles.
/// </summary>
public static class DarkMenuFlyout
{
    public static MenuFlyout Create(PlacementMode placement) =>
        new()
        {
            Placement = placement
        };

    public static MenuItem CreateItem(string header) =>
        new()
        {
            Header = header
        };
}
