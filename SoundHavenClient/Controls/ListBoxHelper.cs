using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace SoundHaven.Controls
{
    public static class ListBoxHelper
    {
        public static readonly AttachedProperty<ICommand> DeleteCommandProperty =
            AvaloniaProperty.RegisterAttached<ListBox, ICommand>(
                "DeleteCommand", typeof(ListBoxHelper));

        public static void SetDeleteCommand(ListBox element, ICommand value)
        {
            element.SetValue(DeleteCommandProperty, value);
        }

        public static ICommand GetDeleteCommand(ListBox element) => element.GetValue(DeleteCommandProperty);
    }
}
