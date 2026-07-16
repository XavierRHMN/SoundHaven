using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
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
        private readonly IUserNotificationService _notifications;
        private readonly ObservableCollection<Song> _emptySongs = new();
        private readonly ObservableCollection<PlaylistTrackRow> _trackRows = new();
        private readonly Bitmap?[] _coverSlots = new Bitmap?[4];

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
                RefreshTracksAndHeader();
                _ = EnsureThumbnailsAsync();
            }
        }

        public bool HasPlaylist => DisplayedPlaylist is not null;

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

        public ObservableCollection<Song> Songs => DisplayedPlaylist?.Songs ?? _emptySongs;

        public ObservableCollection<PlaylistTrackRow> TrackRows => _trackRows;

        public Bitmap? CoverSlot0 => _coverSlots[0];
        public Bitmap? CoverSlot1 => _coverSlots[1];
        public Bitmap? CoverSlot2 => _coverSlots[2];
        public Bitmap? CoverSlot3 => _coverSlots[3];

        public bool HasCoverArt => _coverSlots.Any(slot => slot is not null);

        public bool HasMosaicCover => _coverSlots.Count(slot => slot is not null) >= 2;

        public bool HasSingleCover => HasCoverArt && !HasMosaicCover;

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

            _playbackViewModel.PropertyChanged += OnPlaybackPropertyChanged;

            AddSongCommand = new AsyncRelayCommand(
                AddSongAsync,
                onException: exception => _notifications.ShowError(exception.Message));
            PlayPlaylistCommand = new AsyncRelayCommand(
                PlayPlaylistAsync,
                () => Songs.Count > 0,
                exception => _notifications.ShowError(exception.Message));
            ShufflePlaylistCommand = new AsyncRelayCommand(
                ShufflePlaylistAsync,
                () => Songs.Count > 0,
                exception => _notifications.ShowError(exception.Message));
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
