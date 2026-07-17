using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading.Tasks;
using SoundHaven.Commands;
using SoundHaven.Models;
using SoundHaven.Services;
using SoundHaven.Stores;

namespace SoundHaven.ViewModels;

public sealed class PlayerViewModel : ViewModelBase
{
    private const int MaxHistoryItems = 50;

    private readonly PlaybackViewModel _playbackViewModel;
    private readonly PlaylistStore _playlistStore;
    private readonly IUserNotificationService _notifications;
    private ObservableCollection<Song>? _subscribedSongs;
    private Song? _menuSong;
    private Song? _lastPlayingSong;

    public ObservableCollection<Song> UpNextSongs { get; } = [];

    public ObservableCollection<Song> HistorySongs { get; } = [];

    public ObservableCollection<Playlist> Playlists => _playlistStore.Playlists;

    public string ActivePlaylistName =>
        _playbackViewModel.CurrentPlaylist?.Name ?? "No Active Playlist";

    public Song? PlayerViewSong => _playbackViewModel.CurrentSong;

    public bool HasPlayingSong => PlayerViewSong is not null;

    public bool HasUpNextSongs => UpNextSongs.Count > 0;

    public bool HasHistorySongs => HistorySongs.Count > 0;

    public AsyncRelayCommand<Song> PlaySongCommand { get; }

    public AsyncRelayCommand<Song> PlayHistorySongCommand { get; }

    public AsyncRelayCommand<Song> PlayNextCommand { get; }

    public AsyncRelayCommand<Song> AddToUpNextCommand { get; }

    public RelayCommand<Playlist> AddToPlaylistCommand { get; }

    public RelayCommand CreatePlaylistAndAddSongCommand { get; }

    public PlayerViewModel(
        PlaybackViewModel playbackViewModel,
        PlaylistStore playlistStore,
        IUserNotificationService notifications)
    {
        _playbackViewModel = playbackViewModel
            ?? throw new ArgumentNullException(nameof(playbackViewModel));
        _playlistStore = playlistStore ?? throw new ArgumentNullException(nameof(playlistStore));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _lastPlayingSong = playbackViewModel.CurrentSong;
        _playbackViewModel.PropertyChanged += PlaybackViewModel_PropertyChanged;
        PlaySongCommand = new AsyncRelayCommand<Song>(
            PlaySongAsync,
            song => song is not null,
            exception => _notifications.ShowError(exception.Message));
        PlayHistorySongCommand = new AsyncRelayCommand<Song>(
            PlayHistorySongAsync,
            song => song is not null,
            exception => _notifications.ShowError(exception.Message));
        PlayNextCommand = new AsyncRelayCommand<Song>(
            PlayNextAsync,
            song => song is not null,
            exception => _notifications.ShowError(exception.Message));
        AddToUpNextCommand = new AsyncRelayCommand<Song>(
            AddToUpNextAsync,
            song => song is not null,
            exception => _notifications.ShowError(exception.Message));
        AddToPlaylistCommand = new RelayCommand<Playlist>(
            ExecuteAddToPlaylist,
            playlist => playlist is { Id: > 0 } && _menuSong is not null);
        CreatePlaylistAndAddSongCommand = new RelayCommand(
            ExecuteCreatePlaylistAndAddSong,
            () => _menuSong is not null);
        SubscribeToPlaylistSongs();
        RefreshUpNext();
    }

    public void SetMenuSong(Song song)
    {
        _menuSong = song ?? throw new ArgumentNullException(nameof(song));
        AddToPlaylistCommand.RaiseCanExecuteChanged();
        CreatePlaylistAndAddSongCommand.RaiseCanExecuteChanged();
    }

    public bool MoveUpNext(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex
            || fromIndex < 0
            || toIndex < 0
            || fromIndex >= UpNextSongs.Count
            || toIndex >= UpNextSongs.Count)
        {
            return false;
        }

        if (_subscribedSongs is not null)
        {
            _subscribedSongs.CollectionChanged -= OnPlaylistSongsChanged;
        }

        try
        {
            if (!_playbackViewModel.MoveUpNext(fromIndex, toIndex))
            {
                return false;
            }

            UpNextSongs.Move(fromIndex, toIndex);
            return true;
        }
        finally
        {
            if (_subscribedSongs is not null)
            {
                _subscribedSongs.CollectionChanged += OnPlaylistSongsChanged;
            }
        }
    }

    public bool TryRemoveFromUpNext(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);

        if (_subscribedSongs is not null)
        {
            _subscribedSongs.CollectionChanged -= OnPlaylistSongsChanged;
        }

        try
        {
            bool removed = _playbackViewModel.RemoveFromUpNext(song);
            if (!removed)
            {
                int index = IndexOfUpNext(song);
                removed = index >= 0 && _playbackViewModel.RemoveFromUpNextAt(index);
            }

            if (!removed)
            {
                return false;
            }

            RefreshUpNext();
            return true;
        }
        finally
        {
            if (_subscribedSongs is not null)
            {
                _subscribedSongs.CollectionChanged += OnPlaylistSongsChanged;
            }
        }
    }

    public int IndexOfUpNext(Song song)
    {
        for (int i = 0; i < UpNextSongs.Count; i++)
        {
            if (ReferenceEquals(UpNextSongs[i], song))
            {
                return i;
            }
        }

        return -1;
    }

    public override void Dispose()
    {
        _playbackViewModel.PropertyChanged -= PlaybackViewModel_PropertyChanged;
        UnsubscribeFromPlaylistSongs();
        base.Dispose();
    }

    private Task PlaySongAsync(Song? song)
    {
        return song is null ? Task.CompletedTask : _playbackViewModel.PlayFromBeginning(song);
    }

    private async Task PlayNextAsync(Song? song)
    {
        if (song is null)
        {
            return;
        }

        await _playbackViewModel.PlayNext(song);
        _notifications.ShowInfo($"Queued “{song.Title}” to play next.");
    }

    private async Task AddToUpNextAsync(Song? song)
    {
        if (song is null)
        {
            return;
        }

        await _playbackViewModel.AddToUpNext(song);
        _notifications.ShowInfo($"Added “{song.Title}” to Up Next.");
    }

    /// <summary>
    /// Picking a history entry consumes it: the row leaves the log and the track
    /// moves back into Playing. The track it interrupts is appended to the log
    /// by the normal transition bookkeeping.
    /// </summary>
    private Task PlayHistorySongAsync(Song? song)
    {
        if (song is null)
        {
            return Task.CompletedTask;
        }

        if (HistorySongs.Remove(song))
        {
            OnPropertyChanged(nameof(HasHistorySongs));
        }

        return _playbackViewModel.PlayFromBeginning(song);
    }

    private void ExecuteAddToPlaylist(Playlist? playlist)
    {
        if (playlist is null || _menuSong is null)
        {
            return;
        }

        try
        {
            _playlistStore.AddSongToPlaylist(playlist, _menuSong);
            _notifications.ShowInfo($"Added “{_menuSong.Title}” to “{playlist.Name}”.");
        }
        catch (Exception exception)
        {
            _notifications.ShowError(exception.Message);
        }
    }

    private void ExecuteCreatePlaylistAndAddSong()
    {
        if (_menuSong is null)
        {
            return;
        }

        try
        {
            var playlist = new Playlist
            {
                Name = "New playlist",
                Songs = []
            };
            _playlistStore.AddPlaylist(playlist);
            _playlistStore.AddSongToPlaylist(playlist, _menuSong);
            _notifications.ShowInfo($"Created “{playlist.Name}” and added “{_menuSong.Title}”.");
        }
        catch (Exception exception)
        {
            _notifications.ShowError(exception.Message);
        }
    }

    private void PlaybackViewModel_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlaybackViewModel.CurrentSong):
                OnPropertyChanged(nameof(PlayerViewSong));
                OnPropertyChanged(nameof(HasPlayingSong));
                RefreshUpNext();
                RecordHistoryTransition();
                break;
            case nameof(PlaybackViewModel.CurrentPlaylist):
                OnPropertyChanged(nameof(ActivePlaylistName));
                SubscribeToPlaylistSongs();
                RefreshUpNext();
                break;
        }
    }

    private void SubscribeToPlaylistSongs()
    {
        UnsubscribeFromPlaylistSongs();
        _subscribedSongs = _playbackViewModel.CurrentPlaylist?.Songs;
        if (_subscribedSongs is not null)
        {
            _subscribedSongs.CollectionChanged += OnPlaylistSongsChanged;
        }
    }

    private void UnsubscribeFromPlaylistSongs()
    {
        if (_subscribedSongs is not null)
        {
            _subscribedSongs.CollectionChanged -= OnPlaylistSongsChanged;
            _subscribedSongs = null;
        }
    }

    private void OnPlaylistSongsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshUpNext();
    }

    private void RefreshUpNext()
    {
        UpNextSongs.Clear();
        foreach (Song song in _playbackViewModel.GetUpcomingSongs())
        {
            UpNextSongs.Add(song);
        }

        OnPropertyChanged(nameof(HasUpNextSongs));
    }

    /// <summary>
    /// Up Next -> Playing -> History: the moment a track stops being the current
    /// one it is appended to the bottom of the history log. Replays append again;
    /// the log stays chronological, never deduped or reordered.
    /// </summary>
    private void RecordHistoryTransition()
    {
        Song? current = _playbackViewModel.CurrentSong;
        Song? previous = _lastPlayingSong;
        _lastPlayingSong = current;
        if (previous is null || ReferenceEquals(previous, current))
        {
            return;
        }

        // Clone so a replayed track yields distinct ListBox items.
        HistorySongs.Add(previous.CloneForQueue());
        while (HistorySongs.Count > MaxHistoryItems)
        {
            HistorySongs.RemoveAt(0);
        }

        OnPropertyChanged(nameof(HasHistorySongs));
    }
}
