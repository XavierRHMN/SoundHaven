using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SoundHaven.Models;
using SoundHaven.ViewModels;

namespace SoundHaven.Views;

public partial class PlayerView : UserControl
{
    private Song? _draggingSong;
    private int _dragFromIndex = -1;
    private bool _isDragging;

    public PlayerView()
    {
        InitializeComponent();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (_isDragging)
        {
            return;
        }

        if (DataContext is PlayerViewModel viewModel
            && sender is ListBox { SelectedItem: Song song }
            && !ReferenceEquals(song, viewModel.PlayerViewSong)
            && viewModel.PlaySongCommand.CanExecute(song))
        {
            viewModel.PlaySongCommand.Execute(song);
        }
    }

    private void OnDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control handle
            || handle.DataContext is not Song song
            || DataContext is not PlayerViewModel viewModel)
        {
            return;
        }

        if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        int fromIndex = viewModel.IndexOfUpNext(song);
        if (fromIndex < 0)
        {
            return;
        }

        _draggingSong = song;
        _dragFromIndex = fromIndex;
        _isDragging = true;
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void OnDragHandlePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging
            || _draggingSong is null
            || DataContext is not PlayerViewModel viewModel
            || UpNextList is null
            || sender is not Control handle)
        {
            return;
        }

        Point position = e.GetPosition(UpNextList);
        int targetIndex = GetInsertIndex(UpNextList, position, viewModel.UpNextSongs.Count);
        if (targetIndex > _dragFromIndex)
        {
            targetIndex--;
        }

        targetIndex = Math.Clamp(targetIndex, 0, Math.Max(viewModel.UpNextSongs.Count - 1, 0));
        if (targetIndex != _dragFromIndex && viewModel.MoveUpNext(_dragFromIndex, targetIndex))
        {
            _dragFromIndex = targetIndex;
        }

        e.Handled = true;
    }

    private void OnDragHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        e.Pointer.Capture(null);
        _isDragging = false;
        _draggingSong = null;
        _dragFromIndex = -1;
        e.Handled = true;
    }

    private void OnDragHandlePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isDragging = false;
        _draggingSong = null;
        _dragFromIndex = -1;
    }

    private void OnRemoveFromUpNextPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control
            || control.DataContext is not Song song
            || DataContext is not PlayerViewModel viewModel
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;
        viewModel.TryRemoveFromUpNext(song);
    }

    private static int GetInsertIndex(ListBox listBox, Point position, int itemCount)
    {
        if (itemCount <= 0)
        {
            return 0;
        }

        for (int i = 0; i < itemCount; i++)
        {
            if (listBox.ContainerFromIndex(i) is not Control container)
            {
                continue;
            }

            Point? topLeft = container.TranslatePoint(new Point(0, 0), listBox);
            if (topLeft is null)
            {
                continue;
            }

            double midY = topLeft.Value.Y + (container.Bounds.Height / 2d);
            if (position.Y < midY)
            {
                return i;
            }
        }

        return itemCount;
    }
}
