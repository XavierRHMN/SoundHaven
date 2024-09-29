using Avalonia;
using Avalonia.Controls;
using System.Collections;
using System.Collections.Specialized;

namespace SoundHaven.Helpers
{
    public static class DataGridHelper
    {
        public static readonly AttachedProperty<IList> SelectedItemsProperty =
            AvaloniaProperty.RegisterAttached<DataGrid, IList>(
                "SelectedItems", typeof(DataGridHelper));

        public static void SetSelectedItems(DataGrid element, IList value)
        {
            element.SetValue(SelectedItemsProperty, value);
        }

        public static IList GetSelectedItems(DataGrid element)
        {
            return element.GetValue(SelectedItemsProperty);
        }

        static DataGridHelper()
        {
            SelectedItemsProperty.Changed.AddClassHandler<DataGrid>(
                (x, e) => OnSelectedItemsChanged(x, e));
        }

        private static void OnSelectedItemsChanged(DataGrid dataGrid, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.OldValue is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= OnCollectionChanged;
                dataGrid.SelectionChanged -= OnSelectionChanged;
            }

            if (e.NewValue is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += OnCollectionChanged;
                dataGrid.SelectionChanged += OnSelectionChanged;
            }
        }

        private static void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Handle changes in the bound collection
        }

        private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid dataGrid)
            {
                var selectedItems = GetSelectedItems(dataGrid);
                if (selectedItems != null)
                {
                    foreach (var item in e.RemovedItems)
                    {
                        selectedItems.Remove(item);
                    }
                    foreach (var item in e.AddedItems)
                    {
                        selectedItems.Add(item);
                    }
                }
            }
        }
    }
}
