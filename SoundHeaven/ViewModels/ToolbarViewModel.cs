using Avalonia.Controls;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using SoundHeaven.Models;
using SoundHeaven.Services;
using SoundHeaven.Commands;
using SoundHeaven.Stores;
using System;

namespace SoundHeaven.ViewModels
{
    public class ToolbarViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        private PlaylistViewModel _playlistViewModel;
        private HomeViewModel _homeViewModel;
        private PlayerViewModel _playerViewModel;
        private PlaylistStore _playlistStore;
        private ThemesViewModel _themesViewModel;

        // Current Playlist binding for ToolbarControl.axaml
        private Playlist? _toolbarSelectedPlaylist;
        public Playlist? ToolbarSelectedPlaylist
        {
            get => _toolbarSelectedPlaylist;
            set
            {
                if (_toolbarSelectedPlaylist != value)
                {
                    _toolbarSelectedPlaylist = value;
                    OnPropertyChanged();

                    if (value != null)
                    {
                        UpdatePlaylistViewAndSwitch(value);
                    }
                }
            }
        }

        // Playlists collection
        public ObservableCollection<Playlist> PlaylistCollection => _mainWindowViewModel.PlaylistCollection;

        // Commands
        public RelayCommand ShowHomeViewCommand { get; set; }
        public RelayCommand ShowSearchViewCommand { get; set; }
        public RelayCommand ShowPlaylistViewCommand { get; set; }
        public RelayCommand ShowPlayerViewCommand { get; set; }
        public AsyncRelayCommand CreatePlaylistCommand { get; set; }
        public RelayCommand<Playlist> DeletePlaylistCommand { get; set; }
        public RelayCommand ShowThemesViewCommand { get; set; }

        // Constructor
        public ToolbarViewModel(MainWindowViewModel mainWindowViewModel, PlaylistViewModel playlistViewModel,
                                       HomeViewModel homeViewModel, PlayerViewModel playerViewModel, PlaylistStore playlistStore, ThemesViewModel themesViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;
            _playlistViewModel = playlistViewModel;
            _homeViewModel = homeViewModel;
            _playerViewModel = playerViewModel;
            _playlistStore = playlistStore;
            _themesViewModel = themesViewModel;
            
            ShowHomeViewCommand = new RelayCommand(ShowHomeView);
            ShowSearchViewCommand = new RelayCommand(ShowSearchView);
            ShowPlaylistViewCommand = new RelayCommand(ShowPlaylistView);
            ShowPlayerViewCommand = new RelayCommand(ShowPlayerView);
            ShowThemesViewCommand = new RelayCommand(ShowThemesView);
            CreatePlaylistCommand = new AsyncRelayCommand(CreatePlaylistAsync);
            DeletePlaylistCommand = new RelayCommand<Playlist>(DeletePlaylist);
            

        }

        private bool _isCreatingPlaylist;

        public async Task CreatePlaylistAsync()
        {
            if (_isCreatingPlaylist)
                return;

            _isCreatingPlaylist = true;

            try
            {
                Console.WriteLine("Creating new playlist");

                await Task.Delay(50); 

                var newPlaylist = new Playlist
                {
                    Name = $"Playlist #{PlaylistCollection.Count + 1}",
                    Description = "A new playlist",
                    Songs = new ObservableCollection<Song>()
                };

                _playlistStore.AddPlaylist(newPlaylist);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating playlist: {ex.Message}");
            }
            finally
            {
                _isCreatingPlaylist = false;
            }
        }
        
        private void DeletePlaylist(Playlist playlist)
        {
            if (playlist != null)
            {
                _playlistStore.RemovePlaylist(playlist);
                
                // If the deleted playlist was the currently selected one, deselect it
                if (ToolbarSelectedPlaylist == playlist)
                {
                    ToolbarSelectedPlaylist = null;
                }

                // If we're currently viewing the deleted playlist, switch to home view
                if (_mainWindowViewModel.CurrentViewModel == _playlistViewModel && 
                    _playlistViewModel.DisplayedPlaylist == playlist)
                {
                    ShowHomeView();
                }
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
            _mainWindowViewModel.CurrentViewModel = _homeViewModel;
        }

        private void ShowSearchView()
        {
            // Logic to switch to the Search view
            DeselectCurrentPlaylist();
        }

        private void ShowPlaylistView()
        {
            Console.WriteLine("Switching to PlaylistView");
            _mainWindowViewModel.CurrentViewModel = _playlistViewModel;
        }
        
        private void DeselectCurrentPlaylist()
        {
            ToolbarSelectedPlaylist = null;
        }
    }
}
