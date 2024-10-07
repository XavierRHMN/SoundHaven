using Avalonia.Controls;
using Avalonia.Threading;
using SoundHaven.Commands;
using SoundHaven.Data;
using SoundHaven.Models;
using SoundHaven.Stores;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using SoundHaven.Services;
using System;

namespace SoundHaven.ViewModels
{
    public class ToolbarViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        private PlaylistViewModel _playlistViewModel;
        private HomeViewModel _homeViewModel;
        private PlayerViewModel _playerViewModel;
        private PlaylistStore _playlistStore;
        private SearchViewModel _searchViewModel;
        private ThemesViewModel _themesViewModel;

        // Current Playlist binding for ToolbarControl.axaml
        private Playlist? _toolbarSelectedPlaylist;
        public Playlist? ToolbarSelectedPlaylist
        {
            get => _toolbarSelectedPlaylist;
            set
            {
                if (SetProperty(ref _toolbarSelectedPlaylist, value))
                {
                    UpdatePlaylistViewAndSwitch(value);
                }
            }
        }

        public ObservableCollection<Playlist> PlaylistCollection
        {
            get => _playlistStore.Playlists;
        }

        public RelayCommand ShowHomeViewCommand { get; set; }
        public RelayCommand ShowSearchViewCommand { get; set; }
        public RelayCommand ShowPlaylistViewCommand { get; set; }
        public RelayCommand ShowPlayerViewCommand { get; set; }
        public AsyncRelayCommand CreatePlaylistCommand { get; set; }
        public RelayCommand<Playlist> DeletePlaylistCommand { get; set; }
        public RelayCommand ShowThemesViewCommand { get; set; }

        public ToolbarViewModel(MainWindowViewModel mainWindowViewModel, PlaylistViewModel playlistViewModel,
                                HomeViewModel homeViewModel, PlayerViewModel playerViewModel, PlaylistStore playlistStore,
                                SearchViewModel searchViewModel, ThemesViewModel themesViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;
            _playlistViewModel = playlistViewModel;
            _homeViewModel = homeViewModel;
            _playerViewModel = playerViewModel;
            _playlistStore = playlistStore;
            _themesViewModel = themesViewModel;
            _searchViewModel = searchViewModel;

            ShowHomeViewCommand = new RelayCommand(ShowHomeView);
            ShowSearchViewCommand = new RelayCommand(ShowSearchView);
            ShowPlaylistViewCommand = new RelayCommand(ShowPlaylistView);
            ShowPlayerViewCommand = new RelayCommand(ShowPlayerView);
            ShowThemesViewCommand = new RelayCommand(ShowThemesView);
            CreatePlaylistCommand = new AsyncRelayCommand(CreatePlaylistAsync);
            DeletePlaylistCommand = new RelayCommand<Playlist>(DeletePlaylist);
        }
        
        private async Task CreatePlaylistAsync()
        {
            try
            {
                Console.WriteLine("Creating new playlist");

                await Task.Delay(50);

                var newPlaylist = new Playlist
                {
                    Name = $"Playlist #{PlaylistCollection.Count + 1}",
                    Songs = new ObservableCollection<Song>()
                };
                _playlistStore.AddPlaylist(newPlaylist);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating playlist: {ex.Message}");
            }
        }

        private void DeletePlaylist(Playlist playlist)
        {
            if (playlist != null)
            {
                _playlistStore.RemovePlaylist(playlist);
            }
        }

        private void UpdatePlaylistViewAndSwitch(Playlist playlist)
        {
            if (_playlistViewModel != null)
            {
                _playlistViewModel.DisplayedPlaylist = playlist;
            }
            _mainWindowViewModel.CurrentViewModel = _playlistViewModel;
        }

        private void ShowThemesView()
        {
            Console.WriteLine("Switching to ThemesView");
            DeselectCurrentPlaylist();
            _mainWindowViewModel.CurrentViewModel = _themesViewModel;
        }

        private void ShowPlayerView()
        {
            Console.WriteLine("Switching to PlayerView");
            DeselectCurrentPlaylist();
            _mainWindowViewModel.CurrentViewModel = _playerViewModel;
        }

        // Methods for commands
        private void ShowHomeView()
        {
            Console.WriteLine("Switching to HomeView");
            DeselectCurrentPlaylist();
            SwitchToViewModel(_homeViewModel);
        }

        private void ShowSearchView()
        {
            DeselectCurrentPlaylist();
            SwitchToViewModel(_searchViewModel);
        }

        private void ShowPlaylistView()
        {
            Console.WriteLine("Switching to PlaylistView");
            SwitchToViewModel(_playlistViewModel);
        }

        private void DeselectCurrentPlaylist()
        {
            ToolbarSelectedPlaylist = null;
            _searchViewModel.SelectedSong = null;
        }

        private void SwitchToViewModel(ViewModelBase viewModel)
        {
            _mainWindowViewModel.CurrentViewModel = viewModel;
        }
    }
}
