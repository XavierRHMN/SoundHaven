using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using SoundHaven.Commands;
using SoundHaven.Data;
using SoundHaven.Helpers;
using SoundHaven.Models;
using SoundHaven.Services;

namespace SoundHaven.ViewModels
{
    public class PlaylistViewModel : ViewModelBase
    {
        private readonly PlaybackViewModel _playbackViewModel;
        private readonly IOpenFileDialogService _openFileDialogService;
        private readonly AppDatabase _appDatabase;
        private readonly IUserNotificationService _notifications;
        private readonly ObservableCollection<Song> _emptySongs = new();

        private Playlist? _displayedPlaylist;
        public Playlist? DisplayedPlaylist
        {
            get => _displayedPlaylist;
            set
            {
                if (ReferenceEquals(_displayedPlaylist, value))
                {
                    return;
                }

                Playlist? previousPlaylist = _displayedPlaylist;
                if (previousPlaylist != null)
                {
                    previousPlaylist.PropertyChanged -= OnPlaylistPropertyChanged;
                    foreach (Song song in previousPlaylist.Songs)
                    {
                        song.IsSelected = false;
                    }
                }

                SetProperty(ref _displayedPlaylist, value);

                if (_displayedPlaylist != null)
                {
                    _displayedPlaylist.PropertyChanged += OnPlaylistPropertyChanged;
                }

                SelectedItems.Clear();
                SelectedSong = null;
                IsEditMode = false;
                OnPropertyChanged(nameof(Songs));
            }
        }

        public ObservableCollection<Song> Songs => DisplayedPlaylist?.Songs ?? _emptySongs;

        private bool _isEditMode;
        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (SetProperty(ref _isEditMode, value))
                {
                    OnPropertyChanged(nameof(EditButtonContent));
                }
            }
        }

        public string EditButtonContent
        {
            get => IsEditMode ? "Done" : "Edit";
        }

        private ObservableCollection<object> _selectedItems = new();
        public ObservableCollection<object> SelectedItems
        {
            get => _selectedItems;
            set
            {
                if (SetProperty(ref _selectedItems, value ?? new ObservableCollection<object>()))
                {
                    UpdateSelectedSongs();
                }
            }
        }

        private Song? _selectedSong;
        public Song? SelectedSong
        {
            get => _selectedSong;
            set => SetProperty(ref _selectedSong, value);
        }

        public AsyncRelayCommand AddSongCommand { get; }
        public AsyncRelayCommand<Song> PlaySongCommand { get; }
        public RelayCommand ToggleEditModeCommand { get; }
        public RelayCommand DeleteSelectedSongsCommand { get; }

        public PlaylistViewModel(
            PlaybackViewModel playbackViewModel,
            IOpenFileDialogService openFileDialogService,
            AppDatabase appDatabase,
            IUserNotificationService notifications)
        {
            _playbackViewModel = playbackViewModel ?? throw new ArgumentNullException(nameof(playbackViewModel));
            _openFileDialogService = openFileDialogService ?? throw new ArgumentNullException(nameof(openFileDialogService));
            _appDatabase = appDatabase ?? throw new ArgumentNullException(nameof(appDatabase));
            _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

            AddSongCommand = new AsyncRelayCommand(
                AddSongAsync,
                onException: exception => _notifications.ShowError(exception.Message));
            PlaySongCommand = new AsyncRelayCommand<Song>(
                PlaySongAsync,
                song => song is not null && !IsEditMode,
                exception => _notifications.ShowError(exception.Message));
            ToggleEditModeCommand = new RelayCommand(ToggleEditMode);
            DeleteSelectedSongsCommand = new RelayCommand(DeleteSelectedSongs);
        }

        private async Task AddSongAsync()
        {
            Playlist? playlist = DisplayedPlaylist;
            if (playlist == null || playlist.Id <= 0)
            {
                throw new InvalidOperationException("Select a saved playlist before adding a song.");
            }

            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime applicationLifetime)
            {
                throw new InvalidOperationException("The desktop application window is unavailable.");
            }

            var parentWindow = applicationLifetime.MainWindow
                ?? throw new InvalidOperationException("The desktop application window is unavailable.");

            string? filePath = await _openFileDialogService.ShowOpenFileDialogAsync(parentWindow);
            if (!string.IsNullOrEmpty(filePath))
            {
                var newSong = Mp3ToSongHelper.GetSongFromMp3(filePath);
                if (newSong.Artwork is { } artwork)
                {
                    newSong.SetArtworkData(artwork);
                }

                _appDatabase.AddSongToPlaylist(playlist.Id, newSong);
                if (!playlist.Songs.Any(existing => existing.Id == newSong.Id))
                {
                    playlist.Songs.Add(newSong);
                }

                _notifications.ShowInfo($"Added “{newSong.Title}” to {playlist.Name}.");
            }
        }

        private void ToggleEditMode()
        {
            if (DisplayedPlaylist is not { Id: > 0 })
            {
                IsEditMode = false;
                return;
            }

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
            try
            {
                Playlist? playlist = DisplayedPlaylist;
                if (playlist is not { Id: > 0 })
                {
                    return;
                }

                var songsToRemove = playlist.Songs.Where(song => song.IsSelected).Distinct().ToList();
                _appDatabase.RemoveSongsFromPlaylist(playlist.Id, songsToRemove.Select(song => (long)song.Id));

                foreach (Song song in songsToRemove)
                {
                    song.IsSelected = false;
                    playlist.Songs.Remove(song);
                }

                SelectedItems.Clear();
                IsEditMode = false;
            }
            catch (Exception exception)
            {
                _notifications.ShowError(exception.Message);
            }
        }

        private void UpdateSelectedSongs()
        {
            foreach (Song song in Songs)
            {
                song.IsSelected = SelectedItems.Contains(song);
            }
        }

        private async Task PlaySongAsync(Song? song)
        {
            if (song is not null && DisplayedPlaylist is { } playlist)
            {
                _playbackViewModel.CurrentPlaylist = playlist;
                await _playbackViewModel.PlayFromBeginning(song);
            }
        }

        private void OnPlaylistPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Playlist.Name) &&
                sender is Playlist { Id: > 0 } playlist &&
                ReferenceEquals(playlist, DisplayedPlaylist))
            {
                try
                {
                    _appDatabase.UpdatePlaylistName(playlist.Id, playlist.Name);
                }
                catch (Exception exception)
                {
                    _notifications.ShowError(exception.Message);
                }
            }
        }

        public override void Dispose()
        {
            if (_displayedPlaylist != null)
            {
                _displayedPlaylist.PropertyChanged -= OnPlaylistPropertyChanged;
            }

            base.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
