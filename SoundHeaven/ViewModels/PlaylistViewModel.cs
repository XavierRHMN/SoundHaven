using SoundHeaven.Commands;
using SoundHeaven.Models;
using SoundHeaven.Services;
using SoundHeaven.Stores;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace SoundHeaven.ViewModels
{
    public class PlaylistViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        private AudioPlayerService _audioPlayerService => _mainWindowViewModel.AudioService;
        
            // Current Playlist binding for PlaylistView.axaml
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
                        // Notify that the Songs collection has changed
                        OnPropertyChanged(nameof(Songs));
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
                    OnPropertyChanged();

                    // Update MainWindowViewModel's CurrentSong
                    if (_mainWindowViewModel.CurrentSong != _selectedSong)
                    {
                        _mainWindowViewModel.CurrentSong = _selectedSong;
                    }
                }
            }
        }
        
        public ObservableCollection<Song>? Songs => CurrentPlaylist?.Songs;
        
        // New commands for adding, editing, and deleting songs
        public RelayCommand AddSongCommand { get; }
        public RelayCommand EditSongCommand { get; }
        public RelayCommand DeleteSongCommand { get; }

        public PlaylistViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;

            // Define new commands for song management
            AddSongCommand = new RelayCommand(AddSong);
            EditSongCommand = new RelayCommand(EditSong);
            DeleteSongCommand = new RelayCommand(DeleteSong);

            // Subscribe to changes in MainWindowViewModel.CurrentPlaylist
            _mainWindowViewModel.PropertyChanged += MainWindowViewModel_PropertyChanged;

            // Initialize CurrentPlaylist
            CurrentPlaylist = _mainWindowViewModel.CurrentPlaylist;
            SelectedSong = _mainWindowViewModel.CurrentSong;
            
            if (CurrentPlaylist != null)
            {
                Console.WriteLine($"CurrentPlaylist set to: {CurrentPlaylist.Name}");
            }
            else
            {
                Console.WriteLine("No playlists available.");
            }
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
                Console.WriteLine($"Deleted song: {SelectedSong.Title} from playlist: {CurrentPlaylist.Name}");
                CurrentPlaylist.Songs.Remove(SelectedSong);
                // SelectedSong = null; // Clear selection after deletion
            }
        }
        
        private void MainWindowViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPlaylist))
            {
                CurrentPlaylist = _mainWindowViewModel.CurrentPlaylist;
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.CurrentSong))
            {
                if (SelectedSong != _mainWindowViewModel.CurrentSong)
                {
                    SelectedSong = _mainWindowViewModel.CurrentSong;
                }
            }
        }
    }
}
