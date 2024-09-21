using Avalonia.Threading;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
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
        private readonly AudioPlayerService _audioService;
        private readonly PlaylistStore _playlistStore;
        private readonly MainWindowViewModel _mainWindowViewModel;

        private SongStore _songStore;

        // Current Playlist binding for ToolbarControl.axaml
        private Playlist? _currentPlaylist;
        public Playlist? CurrentPlaylist
        {
            get => _currentPlaylist;
            set
            {
                if (_currentPlaylist != value)
                {
                    _currentPlaylist = value;
                    OnPropertyChanged();

                    // Update MainWindowViewModel's CurrentPlaylist
                    _mainWindowViewModel.CurrentPlaylist = _currentPlaylist;
                    
                    // Switch to PlaylistViewModel
                    _mainWindowViewModel.CurrentViewModel = _mainWindowViewModel.PlaylistViewModel;
                }
            }
        }
        // Playlists collection
        public ObservableCollection<Playlist> PlaylistCollection => _mainWindowViewModel.PlaylistCollection;

        // Commands
        public RelayCommand ShowHomeViewCommand { get; set; }
        public RelayCommand ShowSearchViewCommand { get; set; }
        public RelayCommand ShowPlaylistViewCommand { get; set; }
        public RelayCommand CreatePlaylistCommand { get; set; }


        // Constructor
        public ToolBarControlViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;
            _audioService = mainWindowViewModel.AudioService;
            _playlistStore = new PlaylistStore(_mainWindowViewModel);
            _songStore = new SongStore();

            ShowHomeViewCommand = new RelayCommand(ShowHomeView);
            ShowSearchViewCommand = new RelayCommand(ShowSearchView);
            ShowPlaylistViewCommand = new RelayCommand(ShowPlaylistView);
            CreatePlaylistCommand = new RelayCommand(CreatePlaylist);

            // Set initial CurrentViewModel in MainWindowViewModel
            _mainWindowViewModel.CurrentViewModel = new PlaylistViewModel(_mainWindowViewModel);
            
            // Initialize the PlaylistViewModel if not already done
            if (_mainWindowViewModel.PlaylistViewModel == null)
            {
                _mainWindowViewModel.PlaylistViewModel = new PlaylistViewModel(_mainWindowViewModel);
            }
        }
        
        private void CreatePlaylist()
        {
            Console.WriteLine("Creating new playlist");
            var newPlaylist = new Playlist(_audioService, _mainWindowViewModel)
            {
                Name = $"Playlist {PlaylistCollection.Count + 1}",
                Description = "A new playlist",
                Songs = new ObservableCollection<Song>()
            };
            _mainWindowViewModel.PlaylistStore.AddPlaylist(newPlaylist);
        }

        // Methods for commands
        private void ShowHomeView()
        {
            Console.WriteLine("Switching to HomeView");
            _mainWindowViewModel.CurrentViewModel = new HomeViewModel(_mainWindowViewModel);
        }

        private void ShowSearchView()
        {
            // Logic to switch to the Search view
        }

        private void ShowPlaylistView()
        {
            Console.WriteLine("Switching to PlaylistView");
            _mainWindowViewModel.CurrentViewModel = new PlaylistViewModel(_mainWindowViewModel);
        }
    }
}
