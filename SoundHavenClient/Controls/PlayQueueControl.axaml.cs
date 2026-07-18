using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using SoundHaven.Commands;
using SoundHaven.Helpers;
using SoundHaven.Models;
using SoundHaven.ViewModels;

namespace SoundHaven.Controls;

/// <summary>
/// Dockable play-queue panel (History / Playing / Up Next). DataContext is the
/// shared PlayerViewModel; toggled from the player bar's queue button.
/// </summary>
public partial class PlayQueueControl : UserControl
{
    private Song? _draggingSong;
    private int _dragFromIndex = -1;
    private bool _isDragging;
    private bool _suppressSelection;
    private INotifyCollectionChanged? _historyCollection;

    public PlayQueueControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // History lists the earlier tracks of the queue oldest-first; keep the newest
    // (nearest "Playing") in view so the capped 5-row window shows recent history.
    private void OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        if (_historyCollection is not null)
        {
            _historyCollection.CollectionChanged -= OnHistoryCollectionChanged;
            _historyCollection = null;
        }

        if (DataContext is PlayerViewModel viewModel)
        {
            _historyCollection = viewModel.HistorySongs;
            _historyCollection.CollectionChanged += OnHistoryCollectionChanged;
            ScrollHistoryToEnd();
        }
    }

    private void OnHistoryCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
        => ScrollHistoryToEnd();

    private void ScrollHistoryToEnd()
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                int count = HistoryList.ItemCount;
                if (count > 0)
                {
                    HistoryList.ScrollIntoView(count - 1);
                }
            },
            DispatcherPriority.Background);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (_isDragging || _suppressSelection)
        {
            return;
        }

        if (DataContext is not PlayerViewModel viewModel
            || sender is not ListBox listBox
            || listBox.SelectedItem is not Song song
            || ReferenceEquals(song, viewModel.PlayerViewSong)
            || !viewModel.PlaySongCommand.CanExecute(song))
        {
            return;
        }

        // Playing this track rebuilds Up Next (the song leaves the list). Clear selection
        // and defer play so ListBox isn't mutated mid-SelectionChanged.
        DeferPlay(listBox, viewModel.PlaySongCommand, song);
    }

    private void OnQueuedSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (_suppressSelection)
        {
            return;
        }

        if (DataContext is not PlayerViewModel viewModel
            || sender is not ListBox listBox
            || listBox.SelectedItem is not Song song
            || !viewModel.PlayQueuedSongCommand.CanExecute(song))
        {
            return;
        }

        // Playing a queued track consumes its entry (the list mutates), so defer.
        DeferPlay(listBox, viewModel.PlayQueuedSongCommand, song);
    }

    private void OnRemoveFromQueuePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control
            || control.DataContext is not Song song
            || DataContext is not PlayerViewModel viewModel
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;
        viewModel.TryRemoveFromQueue(song);
    }

    private void OnHistorySelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (_suppressSelection)
        {
            return;
        }

        if (DataContext is not PlayerViewModel viewModel
            || sender is not ListBox listBox
            || listBox.SelectedItem is not Song song
            || !viewModel.PlayHistorySongCommand.CanExecute(song))
        {
            return;
        }

        // Picking a history entry removes it from the log and plays it.
        DeferPlay(listBox, viewModel.PlayHistorySongCommand, song);
    }

    private void DeferPlay(ListBox listBox, AsyncRelayCommand<Song> playCommand, Song song)
    {
        _suppressSelection = true;
        try
        {
            listBox.SelectedItem = null;
        }
        finally
        {
            _suppressSelection = false;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (playCommand.CanExecute(song))
            {
                playCommand.Execute(song);
            }
        });
    }

    private void OnActionsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Keep overflow clicks from selecting the row (which would auto-play).
        e.Handled = true;
    }

    private void OnPlayingOverflowButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button
            || DataContext is not PlayerViewModel viewModel
            || viewModel.PlayerViewSong is not { } song)
        {
            return;
        }

        ShowSongMenu(button, viewModel, song, showAtPointer: false);
        e.Handled = true;
    }

    private void OnRowOverflowButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button
            || button.DataContext is not Song song
            || DataContext is not PlayerViewModel viewModel)
        {
            return;
        }

        ShowSongMenu(button, viewModel, song, showAtPointer: false);
        e.Handled = true;
    }

    private void OnRowRightClickPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            return;
        }

        if (sender is not Control control
            || control.DataContext is not Song song
            || DataContext is not PlayerViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        ShowSongMenu(control, viewModel, song, showAtPointer: true);
    }

    private void OnPlayingCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            return;
        }

        if (sender is not Control control
            || DataContext is not PlayerViewModel { PlayerViewSong: { } song } viewModel)
        {
            return;
        }

        e.Handled = true;
        ShowSongMenu(control, viewModel, song, showAtPointer: true);
    }

    private static void ShowSongMenu(
        Control anchor,
        PlayerViewModel viewModel,
        Song song,
        bool showAtPointer)
    {
        viewModel.SetMenuSong(song);

        var flyout = DarkMenuFlyout.Create(
            showAtPointer ? PlacementMode.Pointer : PlacementMode.BottomEdgeAlignedLeft);

        flyout.Items.Add(new MenuItem
        {
            Header = "Play next",
            Command = viewModel.PlayNextCommand,
            CommandParameter = song
        });

        flyout.Items.Add(new MenuItem
        {
            Header = "Add to queue",
            Command = viewModel.AddToQueueCommand,
            CommandParameter = song
        });

        var addToPlaylist = new MenuItem { Header = "Add to playlist" };
        foreach (Playlist playlist in viewModel.Playlists)
        {
            addToPlaylist.Items.Add(new MenuItem
            {
                Header = playlist.Name,
                Command = viewModel.AddToPlaylistCommand,
                CommandParameter = playlist
            });
        }

        if (viewModel.Playlists.Count > 0)
        {
            addToPlaylist.Items.Add(new Separator());
        }

        addToPlaylist.Items.Add(new MenuItem
        {
            Header = "Create one",
            Command = viewModel.CreatePlaylistAndAddSongCommand,
            Icon = new PathIcon
            {
                Data = StreamGeometry.Parse("M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z"),
                Width = 16,
                Height = 16,
                Foreground = Brushes.White
            }
        });

        flyout.Items.Add(addToPlaylist);
        flyout.ShowAt(anchor, showAtPointer);
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
