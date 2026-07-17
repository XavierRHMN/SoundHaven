using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using SoundHaven.Commands;
using SoundHaven.Models;
using SoundHaven.Services;
using SoundHaven.Stores;

namespace SoundHaven.ViewModels;

public sealed class ToolbarViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly PlaylistViewModel _playlistViewModel;
    private readonly PlaybackViewModel _playbackViewModel;
    private readonly HomeViewModel _homeViewModel;
    private readonly LastFmViewModel _lastFmViewModel;
    private readonly PlayerViewModel _playerViewModel;
    private readonly PlaylistStore _playlistStore;
    private readonly SearchViewModel _searchViewModel;
    private readonly ThemesViewModel _themesViewModel;
    private readonly IUserNotificationService _notifications;
    private Playlist? _toolbarSelectedPlaylist;
    private bool _sortAscending = true;

    public ToolbarViewModel(
        NavigationService navigation,
        PlaylistViewModel playlistViewModel,
        PlaybackViewModel playbackViewModel,
        HomeViewModel homeViewModel,
        LastFmViewModel lastFmViewModel,
        PlayerViewModel playerViewModel,
        PlaylistStore playlistStore,
        SearchViewModel searchViewModel,
        ThemesViewModel themesViewModel,
        IUserNotificationService notifications)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _playlistViewModel = playlistViewModel
            ?? throw new ArgumentNullException(nameof(playlistViewModel));
        _playbackViewModel = playbackViewModel
            ?? throw new ArgumentNullException(nameof(playbackViewModel));
        _homeViewModel = homeViewModel ?? throw new ArgumentNullException(nameof(homeViewModel));
        _lastFmViewModel = lastFmViewModel
            ?? throw new ArgumentNullException(nameof(lastFmViewModel));
        _playerViewModel = playerViewModel
            ?? throw new ArgumentNullException(nameof(playerViewModel));
        _playlistStore = playlistStore ?? throw new ArgumentNullException(nameof(playlistStore));
        _searchViewModel = searchViewModel
            ?? throw new ArgumentNullException(nameof(searchViewModel));
        _themesViewModel = themesViewModel
            ?? throw new ArgumentNullException(nameof(themesViewModel));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

        ShowHomeViewCommand = new RelayCommand(() => ShowView(_homeViewModel));
        ShowSearchViewCommand = new RelayCommand(() => ShowView(_searchViewModel));
        ShowLastFmViewCommand = new RelayCommand(() => ShowView(_lastFmViewModel));
        ShowPlaylistViewCommand = new RelayCommand(() => ShowView(_playlistViewModel));
        ShowPlayerViewCommand = new RelayCommand(() => ShowView(_playerViewModel));
        ShowThemesViewCommand = new RelayCommand(() => ShowView(_themesViewModel));
        CreatePlaylistCommand = new AsyncRelayCommand(
            CreatePlaylistAsync,
            onException: exception => _notifications.ShowError(exception.Message));
        SortPlaylistsCommand = new RelayCommand(SortPlaylists);
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
    }

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

    public RelayCommand ShowSearchViewCommand { get; }

    public RelayCommand ShowLastFmViewCommand { get; }

    public RelayCommand ShowPlaylistViewCommand { get; }

    public RelayCommand ShowPlayerViewCommand { get; }

    public AsyncRelayCommand CreatePlaylistCommand { get; }

    public RelayCommand SortPlaylistsCommand { get; }

    public RelayCommand<Playlist> DeletePlaylistCommand { get; }

    public AsyncRelayCommand<Playlist> PlayNowCommand { get; }

    public AsyncRelayCommand<Playlist> ShufflePlaylistCommand { get; }

    public AsyncRelayCommand<Playlist> PlayNextCommand { get; }

    public AsyncRelayCommand<Playlist> EditPlaylistCommand { get; }

    public RelayCommand ShowThemesViewCommand { get; }

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

    private void SortPlaylists()
    {
        var ordered = _sortAscending
            ? PlaylistCollection.OrderBy(playlist => playlist.Name, StringComparer.OrdinalIgnoreCase).ToList()
            : PlaylistCollection.OrderByDescending(playlist => playlist.Name, StringComparer.OrdinalIgnoreCase).ToList();

        _sortAscending = !_sortAscending;

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
        _searchViewModel.SelectedSong = null;
        _navigation.NavigateTo(viewModel);
    }
}
