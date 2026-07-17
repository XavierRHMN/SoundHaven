using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using SoundHaven.Commands;
using SoundHaven.Models;
using SoundHaven.Services;
using SoundHaven.Stores;

namespace SoundHaven.ViewModels;

public enum PlaylistSortMode
{
    CreatedDate,
    UpdatedDate,
    Alphabetical
}

public sealed class ToolbarViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly PlaylistViewModel _playlistViewModel;
    private readonly PlaybackViewModel _playbackViewModel;
    private readonly HomeViewModel _homeViewModel;
    private readonly PlayerViewModel _playerViewModel;
    private readonly PlaylistStore _playlistStore;
    private readonly IUserNotificationService _notifications;
    private Playlist? _toolbarSelectedPlaylist;
    private PlaylistSortMode _playlistSortMode = PlaylistSortMode.CreatedDate;
    private bool _playlistSortDescending;

    public ToolbarViewModel(
        NavigationService navigation,
        PlaylistViewModel playlistViewModel,
        PlaybackViewModel playbackViewModel,
        HomeViewModel homeViewModel,
        PlayerViewModel playerViewModel,
        PlaylistStore playlistStore,
        IUserNotificationService notifications)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _playlistViewModel = playlistViewModel
            ?? throw new ArgumentNullException(nameof(playlistViewModel));
        _playbackViewModel = playbackViewModel
            ?? throw new ArgumentNullException(nameof(playbackViewModel));
        _homeViewModel = homeViewModel ?? throw new ArgumentNullException(nameof(homeViewModel));
        _playerViewModel = playerViewModel
            ?? throw new ArgumentNullException(nameof(playerViewModel));
        _playlistStore = playlistStore ?? throw new ArgumentNullException(nameof(playlistStore));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

        ShowHomeViewCommand = new RelayCommand(() => ShowView(_homeViewModel));
        ShowPlaylistViewCommand = new RelayCommand(() => ShowView(_playlistViewModel));
        ShowPlayerViewCommand = new RelayCommand(() => ShowView(_playerViewModel));
        CreatePlaylistCommand = new AsyncRelayCommand(
            CreatePlaylistAsync,
            onException: exception => _notifications.ShowError(exception.Message));
        DeletePlaylistCommand = new RelayCommand<Playlist>(DeletePlaylist);
        PlayNowCommand = new AsyncRelayCommand<Playlist>(
            PlayNowAsync,
            playlist => playlist is { Songs.Count: > 0 },
            exception => _notifications.ShowError(exception.Message));
        ShufflePlaylistCommand = new AsyncRelayCommand<Playlist>(
            ShuffleAsync,
            playlist => playlist is { Songs.Count: > 0 },
            exception => _notifications.ShowError(exception.Message));
        PlayNextCommand = new AsyncRelayCommand<Playlist>(
            PlayNextAsync,
            playlist => playlist is { Songs.Count: > 0 },
            exception => _notifications.ShowError(exception.Message));
        EditPlaylistCommand = new AsyncRelayCommand<Playlist>(
            EditPlaylistAsync,
            playlist => playlist is { Id: > 0 },
            exception => _notifications.ShowError(exception.Message));

        _navigation.PropertyChanged += OnNavigationPropertyChanged;
    }

    public bool IsHomeActive => ReferenceEquals(_navigation.CurrentViewModel, _homeViewModel);

    public bool IsPlayerActive => ReferenceEquals(_navigation.CurrentViewModel, _playerViewModel);

    public Playlist? ToolbarSelectedPlaylist
    {
        get => _toolbarSelectedPlaylist;
        set
        {
            if (SetProperty(ref _toolbarSelectedPlaylist, value) && value is not null)
            {
                _playlistViewModel.DisplayedPlaylist = value;
                _navigation.NavigateTo(_playlistViewModel);
            }
        }
    }

    public ObservableCollection<Playlist> PlaylistCollection => _playlistStore.Playlists;

    public RelayCommand ShowHomeViewCommand { get; }

    public RelayCommand ShowPlaylistViewCommand { get; }

    public RelayCommand ShowPlayerViewCommand { get; }

    public AsyncRelayCommand CreatePlaylistCommand { get; }

    public PlaylistSortMode PlaylistSortMode => _playlistSortMode;

    public bool PlaylistSortDescending => _playlistSortDescending;

    public RelayCommand<Playlist> DeletePlaylistCommand { get; }

    public AsyncRelayCommand<Playlist> PlayNowCommand { get; }

    public AsyncRelayCommand<Playlist> ShufflePlaylistCommand { get; }

    public AsyncRelayCommand<Playlist> PlayNextCommand { get; }

    public AsyncRelayCommand<Playlist> EditPlaylistCommand { get; }

    private async Task CreatePlaylistAsync()
    {
        var newPlaylist = new Playlist
        {
            Name = "New playlist"
        };

        if (!await _playlistViewModel.PromptPlaylistDetailsAsync(newPlaylist, isCreating: true))
        {
            return;
        }

        _playlistStore.AddPlaylist(newPlaylist);
        ToolbarSelectedPlaylist = newPlaylist;
        _notifications.ShowInfo("Playlist created.");
    }

    /// <summary>Re-selecting the active mode flips direction; a new mode gets its
    /// natural default (dates newest-first, alphabetical A→Z).</summary>
    public void SortPlaylistsBy(PlaylistSortMode mode)
    {
        if (_playlistSortMode == mode)
        {
            _playlistSortDescending = !_playlistSortDescending;
        }
        else
        {
            _playlistSortMode = mode;
            _playlistSortDescending = mode != PlaylistSortMode.Alphabetical;
        }

        ApplyPlaylistSort();
        OnPropertyChanged(nameof(PlaylistSortMode));
        OnPropertyChanged(nameof(PlaylistSortDescending));
    }

    private void ApplyPlaylistSort()
    {
        var ordered = (_playlistSortMode switch
        {
            PlaylistSortMode.Alphabetical => PlaylistCollection
                .OrderBy(playlist => playlist.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(playlist => playlist.Id),
            PlaylistSortMode.UpdatedDate => PlaylistCollection
                .OrderBy(playlist => playlist.UpdatedAtUtc ?? playlist.CreatedAtUtc ?? DateTime.MinValue)
                .ThenBy(playlist => playlist.Id),
            _ => PlaylistCollection
                .OrderBy(playlist => playlist.CreatedAtUtc ?? DateTime.MinValue)
                .ThenBy(playlist => playlist.Id)
        }).ToList();

        if (_playlistSortDescending)
        {
            ordered.Reverse();
        }

        PlaylistCollection.Clear();
        foreach (Playlist playlist in ordered)
        {
            PlaylistCollection.Add(playlist);
        }
    }

    private async Task PlayNowAsync(Playlist? playlist)
    {
        if (playlist is null || playlist.Songs.Count == 0)
        {
            return;
        }

        ToolbarSelectedPlaylist = playlist;
        _playbackViewModel.IsShuffleEnabled = false;
        _playbackViewModel.CurrentPlaylist = playlist;
        await _playbackViewModel.PlayFromBeginning(playlist.Songs[0]);
    }

    private async Task ShuffleAsync(Playlist? playlist)
    {
        if (playlist is null || playlist.Songs.Count == 0)
        {
            return;
        }

        ToolbarSelectedPlaylist = playlist;
        await _playbackViewModel.PlayShuffledAsync(playlist.Songs, playlist.Name);
    }

    private async Task PlayNextAsync(Playlist? playlist)
    {
        if (playlist is null || playlist.Songs.Count == 0)
        {
            return;
        }

        for (int i = playlist.Songs.Count - 1; i >= 0; i--)
        {
            await _playbackViewModel.PlayNext(playlist.Songs[i]);
        }

        _notifications.ShowInfo($"Queued “{playlist.Name}” to play next.");
    }

    private async Task EditPlaylistAsync(Playlist? playlist)
    {
        if (playlist is null)
        {
            return;
        }

        ToolbarSelectedPlaylist = playlist;
        await _playlistViewModel.EditPlaylistAsync(playlist);
    }

    private void DeletePlaylist(Playlist? playlist)
    {
        if (playlist is null)
        {
            return;
        }

        _playlistStore.RemovePlaylist(playlist);
        if (ReferenceEquals(ToolbarSelectedPlaylist, playlist))
        {
            ToolbarSelectedPlaylist = null;
            _navigation.NavigateTo(_homeViewModel);
        }
    }

    private void ShowView(ViewModelBase viewModel)
    {
        ToolbarSelectedPlaylist = null;
        _navigation.NavigateTo(viewModel);
    }

    private void OnNavigationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NavigationService.CurrentViewModel))
        {
            return;
        }

        OnPropertyChanged(nameof(IsHomeActive));
        OnPropertyChanged(nameof(IsPlayerActive));
    }

    public override void Dispose()
    {
        _navigation.PropertyChanged -= OnNavigationPropertyChanged;
        base.Dispose();
    }
}
