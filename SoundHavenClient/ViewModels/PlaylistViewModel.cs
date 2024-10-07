using Avalonia;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using SoundHaven.Commands;
using SoundHaven.Data;
using SoundHaven.Helpers;
using SoundHaven.Models;
using SoundHaven.Services;
using System.ComponentModel;

namespace SoundHaven.ViewModels
{
    public class PlaylistViewModel : ViewModelBase
    {
        private readonly PlaybackViewModel _playbackViewModel;
        private readonly IOpenFileDialogService _openFileDialogService;
        private MusicDatabase _musicDatabase;

        private Playlist _displayedPlaylist;
        public Playlist DisplayedPlaylist
        {
            get => _displayedPlaylist;
            set
            {
                if (_displayedPlaylist != value)
                {
                    if (_displayedPlaylist != null)
                    {
                        _displayedPlaylist.PropertyChanged -= OnPlaylistPropertyChanged;
                    }

                    _displayedPlaylist = value;

                    if (_displayedPlaylist != null)
                    {
                        _displayedPlaylist.PropertyChanged += OnPlaylistPropertyChanged;
                    }

                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Songs));
                }
            }
        }

        public ObservableCollection<Song> Songs
        {
            get
            {
                return DisplayedPlaylist?.Songs ?? new ObservableCollection<Song>();
            }
        }

        private bool _isEditMode;
        public bool IsEditMode
        {
            get
            {
                return _isEditMode;
            }
            set
            {
                if (_isEditMode != value)
                {
                    _isEditMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EditButtonContent));
                }
            }
        }

        public string EditButtonContent
        {
            get
            {
                return IsEditMode ? "Done" : "Edit";
            }
        }

        private ObservableCollection<object> _selectedItems;
        public ObservableCollection<object> SelectedItems
        {
            get
            {
                return _selectedItems;
            }
            set
            {
                _selectedItems = value;
                OnPropertyChanged();
                UpdateSelectedSongs();
            }
        }

        private Song _selectedSong;
        public Song SelectedSong
        {
            get
            {
                return _selectedSong;
            }
            set
            {
                if (_selectedSong != value)
                {
                    _selectedSong = value;
                    OnPropertyChanged();
                    if (_selectedSong != null && !IsEditMode)
                    {
                        SetCurrentSong(_selectedSong);
                    }
                }
            }
        }

        public AsyncRelayCommand AddSongCommand { get; }
        public RelayCommand ToggleEditModeCommand { get; }
        public RelayCommand DeleteSelectedSongsCommand { get; }

        public PlaylistViewModel(PlaybackViewModel playbackViewModel, IOpenFileDialogService openFileDialogService, MusicDatabase musicDatabase)
        {
            _playbackViewModel = playbackViewModel;
            _openFileDialogService = openFileDialogService;
            _musicDatabase = musicDatabase;
                
            AddSongCommand = new AsyncRelayCommand(AddSongAsync);
            ToggleEditModeCommand = new RelayCommand(ToggleEditMode);
            DeleteSelectedSongsCommand = new RelayCommand(DeleteSelectedSongs);
            SelectedItems = new ObservableCollection<object>();

            // Initialize with an empty playlist if needed
            if (DisplayedPlaylist == null)
            {
                DisplayedPlaylist = new Playlist { Name = "New Playlist", Songs = new ObservableCollection<Song>() };
            }
        }

        private async Task AddSongAsync()
        {
            if (DisplayedPlaylist != null)
            {
                var applicationLifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current.ApplicationLifetime;
                var parentWindow = applicationLifetime.MainWindow;
                if (parentWindow == null)
                {
                    Console.WriteLine("Parent window is not available.");
                    return;
                }

                string? filePath = await _openFileDialogService.ShowOpenFileDialogAsync(parentWindow);
                if (!string.IsNullOrEmpty(filePath))
                {
                    var newSong = Mp3ToSongHelper.GetSongFromMp3(filePath);
                    DisplayedPlaylist.Songs.Add(newSong);
                    newSong.SetArtworkData(newSong.Artwork);
                    _musicDatabase.AddSongToPlaylist(DisplayedPlaylist.Id, newSong);
                    Console.WriteLine($"Added song: {newSong.Title} to playlist: {DisplayedPlaylist.Name}");
                }
            }
            else
            {
                Console.WriteLine("No playlist is currently displayed.");
            }
        }

        private void ToggleEditMode()
        {
            IsEditMode = !IsEditMode;
            if (!IsEditMode)
            {
                // Clear all selections when exiting edit mode
                foreach (var song in Songs)
                {
                    song.IsSelected = false;
                }
                SelectedItems.Clear();
            }
        }

        private void DeleteSelectedSongs()
        {
            if (DisplayedPlaylist != null)
            {
                var songsToRemove = Songs.Where(s => s.IsSelected).ToList();
                foreach (var song in songsToRemove)
                {
                    DisplayedPlaylist.Songs.Remove(song);
                    _musicDatabase.RemoveSongFromPlaylist(DisplayedPlaylist.Id, song.Id);
                    Console.WriteLine($"Deleted song: {song.Title} from playlist: {DisplayedPlaylist.Name}");
                }
                SelectedItems.Clear();
                IsEditMode = false;
            }
        }

        private void UpdateSelectedSongs()
        {
            if (Songs != null)
            {
                foreach (var song in Songs)
                {
                    song.IsSelected = SelectedItems.Contains(song);
                }
            }
        }

        private void SetCurrentSong(Song song)
        {
            if (_playbackViewModel != null)
            {
                _playbackViewModel.CurrentPlaylist = DisplayedPlaylist;
                _playbackViewModel.CurrentSong = song;
            }
        }
        
        private void OnPlaylistPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Playlist.Name))
            {
                _musicDatabase.UpdatePlaylistName(DisplayedPlaylist.Id, DisplayedPlaylist.Name);
            }
        }
    }
}
