using SoundHeaven.Commands;
using SoundHeaven.Models;
using SoundHeaven.Services;
using SoundHeaven.Stores;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace SoundHeaven.ViewModels
{
    public class PlaylistViewModel : ViewModelBase
    {
        private PlaylistStore _playlistStore => _mainWindowViewModel.PlaylistStore;
        private AudioPlayerService _audioPlayerService => _mainWindowViewModel.AudioService;
        private readonly MainWindowViewModel _mainWindowViewModel;

        private Playlist? _currentPlaylist;
        public Playlist? CurrentPlaylist
        {
            get => _currentPlaylist;
            set
            {
                if (_currentPlaylist != value)
                {
                    _currentPlaylist = value;
                    OnPropertyChanged(nameof(CurrentPlaylist));
                }
            }
        }

        // The currently selected song in the DataGrid
        private Song? _selectedSong;
        public Song? SelectedSong
        {
            get => _selectedSong;
            set
            {
                if (_selectedSong != value)
                {
                    _selectedSong = value;
                    _mainWindowViewModel.CurrentSong = _selectedSong;
                    OnPropertyChanged(nameof(SelectedSong));
                }
            }
        }

        public ObservableCollection<Playlist> Playlists => _playlistStore.Playlists;

        // Commands
        public RelayCommand NextPlaylistCommand { get; }
        public RelayCommand PreviousPlaylistCommand { get; }
        public RelayCommand<int> SwitchToPlaylistCommand { get; }

        // New commands for adding, editing, and deleting songs
        public RelayCommand AddSongCommand { get; }
        public RelayCommand EditSongCommand { get; }
        public RelayCommand DeleteSongCommand { get; }

        public PlaylistViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;

            NextPlaylistCommand = new RelayCommand(SwitchToNextPlaylist);
            PreviousPlaylistCommand = new RelayCommand(SwitchToPreviousPlaylist);
            SwitchToPlaylistCommand = new RelayCommand<int>(SwitchToPlaylist);

            // Define new commands for song management
            AddSongCommand = new RelayCommand(AddSong);
            EditSongCommand = new RelayCommand(EditSong);
            DeleteSongCommand = new RelayCommand(DeleteSong);

            // Set CurrentPlaylist to the first playlist if available
            CurrentPlaylist = _playlistStore.Playlists.FirstOrDefault();

            if (CurrentPlaylist != null)
            {
                Console.WriteLine($"CurrentPlaylist set to: {CurrentPlaylist.Name}");
            }
            else
            {
                Console.WriteLine("No playlists available.");
            }
        }

        // Switch to the next playlist
        private void SwitchToNextPlaylist()
        {
            _playlistStore.SwitchToNextPlaylist();
            CurrentPlaylist = _playlistStore.GetCurrentPlaylist();
            Console.WriteLine($"Switched to playlist: {CurrentPlaylist?.Name}");
        }

        // Switch to the previous playlist
        private void SwitchToPreviousPlaylist()
        {
            _playlistStore.SwitchToPreviousPlaylist();
            CurrentPlaylist = _playlistStore.GetCurrentPlaylist();
            Console.WriteLine($"Switched to playlist: {CurrentPlaylist?.Name}");
        }

        // Switch to a specific playlist by index
        private void SwitchToPlaylist(int index)
        {
            _playlistStore.SwitchToPlaylist(index);
            CurrentPlaylist = _playlistStore.GetCurrentPlaylist();
            Console.WriteLine($"Switched to playlist: {CurrentPlaylist?.Name}");
        }

        // Add a new song to the current playlist
        private void AddSong()
        {
            if (CurrentPlaylist != null)
            {
                // Example of adding a new song - you could show a dialog here to gather song information
                var newSong = new Song(_audioPlayerService)
                {
                    Title = "New Song",
                    Artist = "Unknown Artist",
                    Duration = TimeSpan.Zero,
                    FilePath = "path/to/song.mp3"
                };
                CurrentPlaylist.Songs.Add(newSong);
                Console.WriteLine($"Added song: {newSong.Title} to playlist: {CurrentPlaylist.Name}");
            }
        }

        // Edit the currently selected song
        private void EditSong()
        {
            if (SelectedSong != null)
            {
                // Example of editing the selected song - you could show a dialog here to edit song information
                SelectedSong.Title = "Edited Song Title";
                OnPropertyChanged(nameof(SelectedSong));
                Console.WriteLine($"Edited song: {SelectedSong.Title}");
            }
        }

        // Delete the currently selected song from the current playlist
        private void DeleteSong()
        {
            if (CurrentPlaylist != null && SelectedSong != null)
            {
                CurrentPlaylist.Songs.Remove(SelectedSong);
                Console.WriteLine($"Deleted song: {SelectedSong.Title} from playlist: {CurrentPlaylist.Name}");
                SelectedSong = null; // Clear selection after deletion
            }
        }
    }
}
