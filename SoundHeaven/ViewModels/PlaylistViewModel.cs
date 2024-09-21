﻿using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using SoundHeaven.Commands;
using SoundHeaven.Models;
using SoundHeaven.Services;
using SoundHeaven.Stores;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace SoundHeaven.ViewModels
{
    public class PlaylistViewModel : ViewModelBase
    {
        private readonly IOpenFileDialogService _openFileDialogService;
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
        public AsyncRelayCommand AddSongCommand { get; }
        public RelayCommand EditSongCommand { get; }
        public RelayCommand DeleteSongCommand { get; }

        public PlaylistViewModel(MainWindowViewModel mainWindowViewModel, IOpenFileDialogService openFileDialogService)
        {
            _mainWindowViewModel = mainWindowViewModel;
            _openFileDialogService = openFileDialogService;

            // Define new commands for song management
            AddSongCommand = new AsyncRelayCommand(AddSongAsync);
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
        public async Task AddSongAsync()
        {
            if (CurrentPlaylist != null)
            {
                // Get the parent window from MainWindowViewModel
                var applicationLifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current.ApplicationLifetime;
                var parentWindow = applicationLifetime.MainWindow;
                if (parentWindow == null)
                {
                    Console.WriteLine("Parent window is not available.");
                    return;
                }

                // Open the file dialog
                var filePath = await _openFileDialogService.ShowOpenFileDialogAsync(parentWindow);
                if (!string.IsNullOrEmpty(filePath))
                {
                    // Optionally, extract metadata using TagLib#
                    var file = TagLib.File.Create(filePath);
                    var newSong = new Song(_audioPlayerService)
                    {
                        Title = file.Tag.Title ?? System.IO.Path.GetFileNameWithoutExtension(filePath),
                        Artist = string.Join(", ", file.Tag.Performers),
                        Duration = file.Properties.Duration,
                        FilePath = filePath,
                        Year = (int)file.Tag.Year
                    };

                    CurrentPlaylist.Songs.Add(newSong);
                    Console.WriteLine($"Added song: {newSong.Title} to playlist: {CurrentPlaylist.Name}");
                }
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
