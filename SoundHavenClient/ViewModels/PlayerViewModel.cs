using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading.Tasks;
using SoundHaven.Commands;
using SoundHaven.Models;
using SoundHaven.Services;

namespace SoundHaven.ViewModels;

public sealed class PlayerViewModel : ViewModelBase
{
    private readonly PlaybackViewModel _playbackViewModel;
    private readonly IUserNotificationService _notifications;
    private ObservableCollection<Song>? _subscribedSongs;

    public ObservableCollection<Song> UpNextSongs { get; } = [];

    public string ActivePlaylistName =>
        _playbackViewModel.CurrentPlaylist?.Name ?? "No Active Playlist";

    public Song? PlayerViewSong => _playbackViewModel.CurrentSong;

    public bool HasPlayingSong => PlayerViewSong is not null;

    public bool HasUpNextSongs => UpNextSongs.Count > 0;

    public AsyncRelayCommand<Song> PlaySongCommand { get; }

    public PlayerViewModel(
        PlaybackViewModel playbackViewModel,
        IUserNotificationService notifications)
    {
        _playbackViewModel = playbackViewModel
            ?? throw new ArgumentNullException(nameof(playbackViewModel));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _playbackViewModel.PropertyChanged += PlaybackViewModel_PropertyChanged;
        PlaySongCommand = new AsyncRelayCommand<Song>(
            PlaySongAsync,
            song => song is not null,
            exception => _notifications.ShowError(exception.Message));
        SubscribeToPlaylistSongs();
        RefreshUpNext();
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
}
