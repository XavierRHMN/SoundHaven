using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using SoundHaven.Commands;
using SoundHaven.Models;
using SoundHaven.Services;

namespace SoundHaven.ViewModels;

public sealed class PlayerViewModel : ViewModelBase
{
    private readonly PlaybackViewModel _playbackViewModel;
    private readonly IUserNotificationService _notifications;

    public ObservableCollection<Song>? UpNextSongs => _playbackViewModel.CurrentPlaylist?.Songs;

    public string ActivePlaylistName =>
        _playbackViewModel.CurrentPlaylist?.Name ?? "No Active Playlist";

    public Song? PlayerViewSong => _playbackViewModel.CurrentSong;

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
    }

    public override void Dispose()
    {
        _playbackViewModel.PropertyChanged -= PlaybackViewModel_PropertyChanged;
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
                break;
            case nameof(PlaybackViewModel.CurrentPlaylist):
                OnPropertyChanged(nameof(UpNextSongs));
                OnPropertyChanged(nameof(ActivePlaylistName));
                break;
        }
    }
}
