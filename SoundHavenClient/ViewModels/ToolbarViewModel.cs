using System;
using System.Collections.ObjectModel;
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
    private readonly HomeViewModel _homeViewModel;
    private readonly PlayerViewModel _playerViewModel;
    private readonly PlaylistStore _playlistStore;
    private readonly SearchViewModel _searchViewModel;
    private readonly ThemesViewModel _themesViewModel;
    private readonly IUserNotificationService _notifications;
    private Playlist? _toolbarSelectedPlaylist;

    public ToolbarViewModel(
        NavigationService navigation,
        PlaylistViewModel playlistViewModel,
        HomeViewModel homeViewModel,
        PlayerViewModel playerViewModel,
        PlaylistStore playlistStore,
        SearchViewModel searchViewModel,
        ThemesViewModel themesViewModel,
        IUserNotificationService notifications)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _playlistViewModel = playlistViewModel
            ?? throw new ArgumentNullException(nameof(playlistViewModel));
        _homeViewModel = homeViewModel ?? throw new ArgumentNullException(nameof(homeViewModel));
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
        ShowPlaylistViewCommand = new RelayCommand(() => ShowView(_playlistViewModel));
        ShowPlayerViewCommand = new RelayCommand(() => ShowView(_playerViewModel));
        ShowThemesViewCommand = new RelayCommand(() => ShowView(_themesViewModel));
        CreatePlaylistCommand = new AsyncRelayCommand(
            CreatePlaylistAsync,
            onException: exception => _notifications.ShowError(exception.Message));
        DeletePlaylistCommand = new RelayCommand<Playlist>(DeletePlaylist);
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

    public RelayCommand ShowPlaylistViewCommand { get; }

    public RelayCommand ShowPlayerViewCommand { get; }

    public AsyncRelayCommand CreatePlaylistCommand { get; }

    public RelayCommand<Playlist> DeletePlaylistCommand { get; }

    public RelayCommand ShowThemesViewCommand { get; }

    private Task CreatePlaylistAsync()
    {
        var newPlaylist = new Playlist
        {
            Name = $"Playlist #{PlaylistCollection.Count + 1}",
            Songs = new ObservableCollection<Song>()
        };
        _playlistStore.AddPlaylist(newPlaylist);
        ToolbarSelectedPlaylist = newPlaylist;
        return Task.CompletedTask;
    }

    private void DeletePlaylist(Playlist playlist)
    {
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
