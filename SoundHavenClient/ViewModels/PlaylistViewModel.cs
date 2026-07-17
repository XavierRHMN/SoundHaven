using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SoundHaven.Commands;
using SoundHaven.Data;
using SoundHaven.Helpers;
using SoundHaven.Models;
using SoundHaven.Services;

namespace SoundHaven.ViewModels
{
    public sealed class PlaylistTrackRow : ViewModelBase
    {
        public PlaylistTrackRow(int number, Song song, bool isCurrentlyPlaying)
        {
            Number = number;
            Song = song ?? throw new ArgumentNullException(nameof(song));
            IsCurrentlyPlaying = isCurrentlyPlaying;
        }

        public int Number { get; }

        public Song Song { get; }

        private bool _isCurrentlyPlaying;
        public bool IsCurrentlyPlaying
        {
            get => _isCurrentlyPlaying;
            set => SetProperty(ref _isCurrentlyPlaying, value);
        }
    }

    public class PlaylistViewModel : ViewModelBase
    {
        private readonly PlaybackViewModel _playbackViewModel;
        private readonly IOpenFileDialogService _openFileDialogService;
        private readonly AppDatabase _appDatabase;
        private readonly Stores.PlaylistStore _playlistStore;
        private readonly IUserNotificationService _notifications;
        private readonly IAlbumArtService _albumArtService;
        private readonly ObservableCollection<Song> _emptySongs = new();
        private readonly ObservableCollection<PlaylistTrackRow> _trackRows = new();
        private readonly Bitmap?[] _coverSlots = new Bitmap?[4];
        private Song? _menuSong;

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
                    previousPlaylist.Songs.CollectionChanged -= OnSongsCollectionChanged;
                    foreach (Song song in previousPlaylist.Songs)
                    {
                        song.IsSelected = false;
                        song.PropertyChanged -= OnSongPropertyChanged;
                    }
                }

                SetProperty(ref _displayedPlaylist, value);

                if (_displayedPlaylist != null)
                {
                    _displayedPlaylist.PropertyChanged += OnPlaylistPropertyChanged;
                    _displayedPlaylist.Songs.CollectionChanged += OnSongsCollectionChanged;
                    foreach (Song song in _displayedPlaylist.Songs)
                    {
                        song.PropertyChanged += OnSongPropertyChanged;
                    }
                }

                SelectedItems.Clear();
                SelectedSong = null;
                IsEditMode = false;
                FilterText = string.Empty;
                OnPropertyChanged(nameof(Songs));
                OnPropertyChanged(nameof(HasPlaylist));
                OnPropertyChanged(nameof(PlaylistName));
                OnPropertyChanged(nameof(PlaylistDescription));
                OnPropertyChanged(nameof(HasPlaylistDescription));
                OnPropertyChanged(nameof(IsLikedSongsPlaylist));
                RefreshTracksAndHeader();
                // These commands' CanExecute reads the displayed playlist, so a
                // playlist swap must re-query them (navigation swaps the whole
                // collection without ever raising CollectionChanged).
                PlayPlaylistCommand?.RaiseCanExecuteChanged();
                ShufflePlaylistCommand?.RaiseCanExecuteChanged();
                PlayPlaylistNextCommand?.RaiseCanExecuteChanged();
                AddSongCommand?.RaiseCanExecuteChanged();
                EnterRemoveSongsCommand?.RaiseCanExecuteChanged();
                RemoveSongFromPlaylistCommand?.RaiseCanExecuteChanged();
                OpenEditPlaylistCommand?.RaiseCanExecuteChanged();
                _ = EnsureThumbnailsAsync();
                _ = ResolveMissingYearsAsync();
            }
        }

        public bool HasPlaylist => DisplayedPlaylist is not null;

        /// <summary>True when the shown playlist is the system Liked Songs, which
        /// displays the eighth-note thumbnail instead of song artwork.</summary>
        public bool IsLikedSongsPlaylist => DisplayedPlaylist?.IsLikedSongs == true;

        public string PlaylistName
        {
            get => DisplayedPlaylist?.Name ?? "No Playlist Selected";
            set
            {
                if (DisplayedPlaylist is null)
                {
                    return;
                }

                DisplayedPlaylist.Name = value ?? string.Empty;
            }
        }

        public string PlaylistDescription => DisplayedPlaylist?.Description?.Trim() ?? string.Empty;

        public bool HasPlaylistDescription => !string.IsNullOrWhiteSpace(PlaylistDescription);

        public ObservableCollection<Song> Songs => DisplayedPlaylist?.Songs ?? _emptySongs;

        public ObservableCollection<PlaylistTrackRow> TrackRows => _trackRows;

        public Bitmap? CoverSlot0 => DisplayedPlaylist?.HasCustomCover == true
            ? DisplayedPlaylist.CoverImage
            : _coverSlots[0];
        public Bitmap? CoverSlot1 => DisplayedPlaylist?.HasCustomCover == true ? null : _coverSlots[1];
        public Bitmap? CoverSlot2 => DisplayedPlaylist?.HasCustomCover == true ? null : _coverSlots[2];
        public Bitmap? CoverSlot3 => DisplayedPlaylist?.HasCustomCover == true ? null : _coverSlots[3];

        public bool HasCoverArt =>
            DisplayedPlaylist?.HasCustomCover == true
            || _coverSlots.Any(slot => slot is not null);

        public bool HasMosaicCover =>
            DisplayedPlaylist?.HasCustomCover != true
            && Songs.Count >= 4
            && _coverSlots[0] is not null;

        public bool HasSingleCover =>
            DisplayedPlaylist?.HasCustomCover == true
            || (HasCoverArt && !HasMosaicCover);

        public string TrackStatsText
        {
            get
            {
                int count = Songs.Count;
                if (count == 0)
                {
                    return "0 TRACKS";
                }

                TimeSpan total = TimeSpan.FromTicks(Songs.Sum(song => song.Duration.Ticks));
                string trackLabel = count == 1 ? "TRACK" : "TRACKS";
                return $"{count} {trackLabel} ({FormatDuration(total)})";
            }
        }

        private string _filterText = string.Empty;
        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value ?? string.Empty))
                {
                    RebuildTrackRows();
                }
            }
        }

        private bool _isEditMode;
        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (SetProperty(ref _isEditMode, value))
                {
                    OnPropertyChanged(nameof(EditButtonContent));
                    PlaySongCommand.RaiseCanExecuteChanged();
                    PlayPlaylistNextCommand.RaiseCanExecuteChanged();
                    AddSongCommand.RaiseCanExecuteChanged();
                    OpenEditPlaylistCommand.RaiseCanExecuteChanged();
                    EnterRemoveSongsCommand.RaiseCanExecuteChanged();
                    CancelRemoveSongsCommand.RaiseCanExecuteChanged();
                    DeleteSelectedSongsCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string EditButtonContent => IsEditMode ? "Done" : "Edit";

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

        private PlaylistTrackRow? _selectedTrackRow;
        public PlaylistTrackRow? SelectedTrackRow
        {
            get => _selectedTrackRow;
            set
            {
                if (SetProperty(ref _selectedTrackRow, value))
                {
                    SelectedSong = value?.Song;
                }
            }
        }

        public AsyncRelayCommand AddSongCommand { get; }
        public AsyncRelayCommand PlayPlaylistCommand { get; }
        public AsyncRelayCommand ShufflePlaylistCommand { get; }
        public AsyncRelayCommand PlayPlaylistNextCommand { get; }
        public AsyncRelayCommand<Song> PlaySongCommand { get; }
        public AsyncRelayCommand<Song> PlaySongNextCommand { get; }
        public AsyncRelayCommand<Song> AddToUpNextCommand { get; }
        public RelayCommand<Playlist> AddMenuSongToPlaylistCommand { get; }
        public RelayCommand<Song> RemoveSongFromPlaylistCommand { get; }
        public AsyncRelayCommand OpenEditPlaylistCommand { get; }
        public RelayCommand EnterRemoveSongsCommand { get; }
        public RelayCommand CancelRemoveSongsCommand { get; }
        public RelayCommand DeleteSelectedSongsCommand { get; }

        /// <summary>Playlists the song context menu can add to (from the shared store).</summary>
        public ObservableCollection<Playlist> AllPlaylists => _playlistStore.Playlists;

        public PlaylistViewModel(
            PlaybackViewModel playbackViewModel,
            IOpenFileDialogService openFileDialogService,
            AppDatabase appDatabase,
            Stores.PlaylistStore playlistStore,
            IUserNotificationService notifications,
            IAlbumArtService albumArtService)
        {
            _playbackViewModel = playbackViewModel ?? throw new ArgumentNullException(nameof(playbackViewModel));
            _openFileDialogService = openFileDialogService ?? throw new ArgumentNullException(nameof(openFileDialogService));
            _appDatabase = appDatabase ?? throw new ArgumentNullException(nameof(appDatabase));
            _playlistStore = playlistStore ?? throw new ArgumentNullException(nameof(playlistStore));
            _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            _albumArtService = albumArtService ?? throw new ArgumentNullException(nameof(albumArtService));

            _playbackViewModel.PropertyChanged += OnPlaybackPropertyChanged;

            AddSongCommand = new AsyncRelayCommand(
                AddSongAsync,
                () => DisplayedPlaylist is { Id: > 0 } && !IsEditMode,
                exception => _notifications.ShowError(exception.Message));
            PlayPlaylistCommand = new AsyncRelayCommand(
                PlayPlaylistAsync,
                () => Songs.Count > 0,
                exception => _notifications.ShowError(exception.Message));
            ShufflePlaylistCommand = new AsyncRelayCommand(
                ShufflePlaylistAsync,
                () => Songs.Count > 0,
                exception => _notifications.ShowError(exception.Message));
            PlayPlaylistNextCommand = new AsyncRelayCommand(
                PlayPlaylistNextAsync,
                () => Songs.Count > 0 && !IsEditMode,
                exception => _notifications.ShowError(exception.Message));
            PlaySongCommand = new AsyncRelayCommand<Song>(
                PlaySongAsync,
                song => song is not null && !IsEditMode,
                exception => _notifications.ShowError(exception.Message));
            PlaySongNextCommand = new AsyncRelayCommand<Song>(
                PlaySongNextAsync,
                song => song is not null && !IsEditMode,
                exception => _notifications.ShowError(exception.Message));
            AddToUpNextCommand = new AsyncRelayCommand<Song>(
                AddToUpNextAsync,
                song => song is not null && !IsEditMode,
                exception => _notifications.ShowError(exception.Message));
            AddMenuSongToPlaylistCommand = new RelayCommand<Playlist>(
                AddMenuSongToPlaylist,
                playlist => playlist is { Id: > 0 } && _menuSong is not null);
            RemoveSongFromPlaylistCommand = new RelayCommand<Song>(
                RemoveSongFromPlaylist,
                song => song is not null && DisplayedPlaylist is { Id: > 0 });
            OpenEditPlaylistCommand = new AsyncRelayCommand(
                () => EditPlaylistAsync(DisplayedPlaylist),
                () => DisplayedPlaylist is { Id: > 0 } && !IsEditMode,
                exception => _notifications.ShowError(exception.Message));
            EnterRemoveSongsCommand = new RelayCommand(EnterRemoveSongsMode, () => DisplayedPlaylist is { Id: > 0 } && !IsEditMode);
            CancelRemoveSongsCommand = new RelayCommand(CancelRemoveSongsMode, () => IsEditMode);
            DeleteSelectedSongsCommand = new RelayCommand(DeleteSelectedSongs, () => IsEditMode);
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

        private async Task PlayPlaylistAsync()
        {
            Playlist? playlist = DisplayedPlaylist;
            if (playlist is null || playlist.Songs.Count == 0)
            {
                return;
            }

            _playbackViewModel.IsShuffleEnabled = false;
            _playbackViewModel.CurrentPlaylist = playlist;
            await _playbackViewModel.PlayFromBeginning(playlist.Songs[0]);
        }

        private async Task ShufflePlaylistAsync()
        {
            Playlist? playlist = DisplayedPlaylist;
            if (playlist is null || playlist.Songs.Count == 0)
            {
                return;
            }

            await _playbackViewModel.PlayShuffledAsync(playlist.Songs, playlist.Name);
        }

        private async Task PlayPlaylistNextAsync()
        {
            Playlist? playlist = DisplayedPlaylist;
            if (playlist is null || playlist.Songs.Count == 0)
            {
                return;
            }

            // Insert in reverse so playlist order is preserved at the front of Up Next.
            for (int i = playlist.Songs.Count - 1; i >= 0; i--)
            {
                await _playbackViewModel.PlayNext(playlist.Songs[i]);
            }

            _notifications.ShowInfo($"Queued “{playlist.Name}” to play next.");
        }

        public async Task EditPlaylistAsync(Playlist? playlist)
        {
            playlist ??= DisplayedPlaylist;
            if (playlist is not { Id: > 0 })
            {
                return;
            }

            if (!await PromptPlaylistDetailsAsync(playlist, isCreating: false))
            {
                return;
            }

            _appDatabase.UpdatePlaylistDetails(
                playlist.Id,
                playlist.Name,
                playlist.Description,
                playlist.CoverImageData);
            playlist.UpdatedAtUtc = DateTime.UtcNow;

            if (ReferenceEquals(DisplayedPlaylist, playlist))
            {
                OnPropertyChanged(nameof(PlaylistName));
                OnPropertyChanged(nameof(PlaylistDescription));
                OnPropertyChanged(nameof(HasPlaylistDescription));
                RefreshCoverSlots();
            }
            else
            {
                playlist.RefreshSidebarCovers();
            }

            _notifications.ShowInfo("Playlist updated.");
        }

        public async Task<bool> PromptPlaylistDetailsAsync(Playlist playlist, bool isCreating)
        {
            ArgumentNullException.ThrowIfNull(playlist);

            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime applicationLifetime
                || applicationLifetime.MainWindow is not { } parentWindow)
            {
                throw new InvalidOperationException("The desktop application window is unavailable.");
            }

            var dialog = new Views.EditPlaylistWindow();
            var editViewModel = new EditPlaylistViewModel(
                playlist,
                _openFileDialogService,
                dialog,
                isCreating);
            dialog.DataContext = editViewModel;
            editViewModel.CloseRequested += (_, saved) => dialog.Close(saved);

            bool saved = await dialog.ShowDialog<bool>(parentWindow);
            if (!saved)
            {
                return false;
            }

            playlist.Name = editViewModel.SavedTitle;
            playlist.Description = editViewModel.SavedDescription;
            playlist.CoverImageData = editViewModel.SavedCoverImageData;
            return true;
        }

        private void EnterRemoveSongsMode()
        {
            if (DisplayedPlaylist is not { Id: > 0 })
            {
                return;
            }

            IsEditMode = true;
        }

        private void CancelRemoveSongsMode()
        {
            if (!IsEditMode)
            {
                return;
            }

            foreach (Song song in Songs)
            {
                song.IsSelected = false;
            }

            SelectedItems.Clear();
            IsEditMode = false;
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
                if (songsToRemove.Count == 0)
                {
                    _notifications.ShowInfo("Select one or more songs to remove.");
                    return;
                }

                _appDatabase.RemoveSongsFromPlaylist(playlist.Id, songsToRemove.Select(song => (long)song.Id));
                playlist.UpdatedAtUtc = DateTime.UtcNow;

                foreach (Song song in songsToRemove)
                {
                    song.IsSelected = false;
                    playlist.Songs.Remove(song);
                }

                SelectedItems.Clear();
                IsEditMode = false;
                _notifications.ShowInfo(
                    songsToRemove.Count == 1
                        ? "Removed 1 song from the playlist."
                        : $"Removed {songsToRemove.Count} songs from the playlist.");
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
                song.IsSelected = SelectedItems.Contains(song)
                    || SelectedItems.OfType<PlaylistTrackRow>().Any(row => ReferenceEquals(row.Song, song));
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

        private async Task PlaySongNextAsync(Song? song)
        {
            if (song is null)
            {
                return;
            }

            await _playbackViewModel.PlayNext(song);
            _notifications.ShowInfo($"Queued “{song.Title}” to play next.");
        }

        private async Task AddToUpNextAsync(Song? song)
        {
            if (song is null)
            {
                return;
            }

            await _playbackViewModel.AddToUpNext(song);
            _notifications.ShowInfo($"Added “{song.Title}” to Up Next.");
        }

        /// <summary>Remembers the row a context menu was opened for.</summary>
        public void SetMenuSong(Song song)
        {
            _menuSong = song ?? throw new ArgumentNullException(nameof(song));
            AddMenuSongToPlaylistCommand.RaiseCanExecuteChanged();
        }

        private void AddMenuSongToPlaylist(Playlist? playlist)
        {
            if (playlist is null || _menuSong is null)
            {
                return;
            }

            try
            {
                _playlistStore.AddSongToPlaylist(playlist, _menuSong);
                _notifications.ShowInfo($"Added “{_menuSong.Title}” to “{playlist.Name}”.");
            }
            catch (Exception exception)
            {
                _notifications.ShowError(exception.Message);
            }
        }

        private void RemoveSongFromPlaylist(Song? song)
        {
            if (song is null || DisplayedPlaylist is not { Id: > 0 } playlist)
            {
                return;
            }

            try
            {
                _appDatabase.RemoveSongsFromPlaylist(playlist.Id, [song.Id]);
                playlist.UpdatedAtUtc = DateTime.UtcNow;
                song.IsSelected = false;
                playlist.Songs.Remove(song);
                _notifications.ShowInfo($"Removed “{song.Title}” from “{playlist.Name}”.");
            }
            catch (Exception exception)
            {
                _notifications.ShowError(exception.Message);
            }
        }

        private void OnPlaylistPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Playlist.Name) &&
                sender is Playlist { Id: > 0 } playlist &&
                ReferenceEquals(playlist, DisplayedPlaylist))
            {
                OnPropertyChanged(nameof(PlaylistName));
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

        private void OnSongsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (Song song in e.OldItems.OfType<Song>())
                {
                    song.PropertyChanged -= OnSongPropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (Song song in e.NewItems.OfType<Song>())
                {
                    song.PropertyChanged += OnSongPropertyChanged;
                }
            }

            RefreshTracksAndHeader();
            PlayPlaylistCommand.RaiseCanExecuteChanged();
            ShufflePlaylistCommand.RaiseCanExecuteChanged();
            _ = EnsureThumbnailsAsync();
        }

        private void OnSongPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(Song.Artwork) or nameof(Song.Duration)
                or nameof(Song.Title) or nameof(Song.Artist) or nameof(Song.Album))
            {
                if (e.PropertyName == nameof(Song.Artwork))
                {
                    RefreshCoverSlots();
                }

                if (e.PropertyName == nameof(Song.Duration))
                {
                    OnPropertyChanged(nameof(TrackStatsText));
                }

                if (e.PropertyName is nameof(Song.Title) or nameof(Song.Artist) or nameof(Song.Album)
                    && !string.IsNullOrWhiteSpace(FilterText))
                {
                    RebuildTrackRows();
                }
            }
        }

        private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlaybackViewModel.CurrentSong))
            {
                UpdatePlayingHighlights();
            }
        }

        private void RefreshTracksAndHeader()
        {
            RebuildTrackRows();
            RefreshCoverSlots();
            OnPropertyChanged(nameof(TrackStatsText));
        }

        private void RebuildTrackRows()
        {
            Song? selected = SelectedSong;
            _trackRows.Clear();

            string filter = FilterText.Trim();
            var filtered = string.IsNullOrEmpty(filter)
                ? Songs.ToList()
                : Songs.Where(song => MatchesFilter(song, filter)).ToList();

            Song? current = _playbackViewModel.CurrentSong;
            for (int i = 0; i < filtered.Count; i++)
            {
                Song song = filtered[i];
                bool isPlaying = current is not null && ReferenceEquals(current, song);
                _trackRows.Add(new PlaylistTrackRow(i + 1, song, isPlaying));
            }

            SelectedTrackRow = selected is null
                ? null
                : _trackRows.FirstOrDefault(row => ReferenceEquals(row.Song, selected));
        }

        private void UpdatePlayingHighlights()
        {
            Song? current = _playbackViewModel.CurrentSong;
            foreach (PlaylistTrackRow row in _trackRows)
            {
                row.IsCurrentlyPlaying = current is not null && ReferenceEquals(current, row.Song);
            }
        }

        private void RefreshCoverSlots()
        {
            var artworks = Songs
                .Select(song => song.Artwork)
                .Where(artwork => artwork is not null)
                .Take(4)
                .Cast<Bitmap>()
                .ToList();

            for (int i = 0; i < _coverSlots.Length; i++)
            {
                _coverSlots[i] = i < artworks.Count ? artworks[i] : null;
            }

            OnPropertyChanged(nameof(CoverSlot0));
            OnPropertyChanged(nameof(CoverSlot1));
            OnPropertyChanged(nameof(CoverSlot2));
            OnPropertyChanged(nameof(CoverSlot3));
            OnPropertyChanged(nameof(HasCoverArt));
            OnPropertyChanged(nameof(HasMosaicCover));
            OnPropertyChanged(nameof(HasSingleCover));
        }

        private async Task EnsureThumbnailsAsync()
        {
            var songs = Songs.Where(song => song.Artwork is null).Take(12).ToList();
            foreach (Song song in songs)
            {
                try
                {
                    await song.LoadThumbnailAsync();
                }
                catch
                {
                    // Thumbnail load is best-effort for playlist cover/list art.
                }
            }
        }

        // Fill in release years the same way the player bar/search do, so the
        // playlist's YEAR column isn't blank for YouTube-sourced tracks.
        private async Task ResolveMissingYearsAsync()
        {
            var songs = Songs
                .Where(song => song.Year is null
                    && !string.IsNullOrWhiteSpace(song.Title)
                    && !string.IsNullOrWhiteSpace(song.Artist))
                .Take(60)
                .ToList();

            foreach (Song song in songs)
            {
                try
                {
                    int? year = await _albumArtService.GetTrackYearAsync(song.Artist, song.Title);
                    if (year is not null)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => song.Year ??= year);
                    }
                }
                catch
                {
                    // The year is decorative and must never disrupt the playlist.
                }
            }
        }

        private static bool MatchesFilter(Song song, string filter)
        {
            return Contains(song.Title, filter)
                || Contains(song.Artist, filter)
                || Contains(song.Album, filter);
        }

        private static bool Contains(string? value, string filter) =>
            !string.IsNullOrEmpty(value)
            && value.Contains(filter, StringComparison.OrdinalIgnoreCase);

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
            {
                return $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}";
            }

            return $"{duration.Minutes}:{duration.Seconds:D2}";
        }

        public override void Dispose()
        {
            _playbackViewModel.PropertyChanged -= OnPlaybackPropertyChanged;

            if (_displayedPlaylist != null)
            {
                _displayedPlaylist.PropertyChanged -= OnPlaylistPropertyChanged;
                _displayedPlaylist.Songs.CollectionChanged -= OnSongsCollectionChanged;
                foreach (Song song in _displayedPlaylist.Songs)
                {
                    song.PropertyChanged -= OnSongPropertyChanged;
                }
            }

            base.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
