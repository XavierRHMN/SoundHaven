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
        // Current Playlist binding
        private Playlist? _currentPlaylist;
        public Playlist? CurrentPlaylist
        {
            get
            {
                return _currentPlaylist;
            }
            set
            {
                if (_currentPlaylist != value)
                {
                    _currentPlaylist = value;
                    OnPropertyChanged();
                }
            }
        }
        // Playlists collection
        public ObservableCollection<Playlist> PlaylistCollection => _playlistStore.Playlists;

        // Commands
        public RelayCommand ShowHomeViewCommand { get; set; }
        public RelayCommand ShowSearchViewCommand { get; set; }
        public RelayCommand ShowPlaylistViewCommand { get; set; }
        public RelayCommand CreatePlaylistCommand { get; set; }

        // Constructor
        public ToolBarControlViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _audioService = new AudioPlayerService();
            _playlistStore = new PlaylistStore(_audioService);
            _mainWindowViewModel = mainWindowViewModel;
            _songStore = new SongStore();

            ShowHomeViewCommand = new RelayCommand(ShowHomeView);
            ShowSearchViewCommand = new RelayCommand(ShowSearchView);
            ShowPlaylistViewCommand = new RelayCommand(ShowPlaylistView);
            CreatePlaylistCommand = new RelayCommand(CreatePlaylist);

            // Load songs and add example playlist
            _songStore.LoadSongs();

            var example = new Playlist(_audioService)
            {
                Name = "example",
                Songs = _songStore.Songs
            };
            _playlistStore.AddPlaylist(example);

            // Set initial CurrentViewModel in MainWindowViewModel
            _mainWindowViewModel.CurrentViewModel = new PlaylistViewModel(_playlistStore, _audioService, _mainWindowViewModel);
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
            _mainWindowViewModel.CurrentViewModel = new PlaylistViewModel(_playlistStore, _audioService, _mainWindowViewModel);
        }
        
        private void CreatePlaylist()
        {
            Console.WriteLine("Creating new playlist");
            var newPlaylist = new Playlist(_audioService)
            {
                Name = $"Playlist {PlaylistCollection.Count + 1}",
                Description = "A new playlist",
                Songs = new ObservableCollection<Song>()
            };
            _playlistStore.AddPlaylist(newPlaylist);
            CurrentPlaylist = newPlaylist; // Optionally set the newly created playlist as current
        }
    }
}
