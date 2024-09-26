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
    public class ToolBarControlViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainWindowViewModel;

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


        // Constructor
        public ToolBarControlViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;

            ShowHomeViewCommand = new RelayCommand(ShowHomeView);
            ShowSearchViewCommand = new RelayCommand(ShowSearchView);
            ShowPlaylistViewCommand = new RelayCommand(ShowPlaylistView);
            ShowPlayerViewCommand = new RelayCommand(ShowPlayerView);
            CreatePlaylistCommand = new AsyncRelayCommand(CreatePlaylistAsync);
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

                _mainWindowViewModel.PlaylistStore.AddPlaylist(newPlaylist);
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
        
        private void UpdatePlaylistViewAndSwitch(Playlist playlist)
        {
            if (_mainWindowViewModel.PlaylistViewModel != null)
            {
                _mainWindowViewModel.PlaylistViewModel.DisplayedPlaylist = playlist;
            }
            _mainWindowViewModel.CurrentViewModel = _mainWindowViewModel.PlaylistViewModel;
        }
        
        private void ShowPlayerView()
        {
            Console.WriteLine("Switching to PlayerView");
            DeselectCurrentPlaylist();
            _mainWindowViewModel.CurrentViewModel = _mainWindowViewModel.PlayerViewModel;
        }
        
        // Methods for commands
        private void ShowHomeView()
        {
            Console.WriteLine("Switching to HomeView");
            DeselectCurrentPlaylist();
            _mainWindowViewModel.CurrentViewModel = _mainWindowViewModel.HomeViewModel;
        }

        private void ShowSearchView()
        {
            // Logic to switch to the Search view
            DeselectCurrentPlaylist();
        }

        private void ShowPlaylistView()
        {
            Console.WriteLine("Switching to PlaylistView");
            _mainWindowViewModel.CurrentViewModel = _mainWindowViewModel.PlaylistViewModel;
        }
        
        private void DeselectCurrentPlaylist()
        {
            ToolbarSelectedPlaylist = null;
        }
    }
}
