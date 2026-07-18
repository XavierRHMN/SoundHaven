using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
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

        private bool _isLiked;
        public bool IsLiked
        {
            get => _isLiked;
            set => SetProperty(ref _isLiked, value);
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
        private readonly IYouTubeMediaService _youTubeMediaService;
        private CancellationTokenSource? _downloadCts;
        private Playlist? _downloadingPlaylist;
        private DownloadState _playlistDownloadState = DownloadState.NotDownloaded;
        private double _downloadAllProgress;
        private bool _showDownloadButton;
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
                OnPropertyChanged(nameof(IsDownloadsPlaylist));
                OnPropertyChanged(nameof(IsSystemPlaylist));
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
                RefreshDownloadState();
            }
        }

        public bool HasPlaylist => DisplayedPlaylist is not null;

        /// <summary>True when the shown playlist is the system Liked Songs, which
        /// displays the eighth-note thumbnail instead of song artwork.</summary>
        public bool IsLikedSongsPlaylist => DisplayedPlaylist?.IsLikedSongs == true;

        /// <summary>True when the shown playlist is the system Downloaded Songs, which
        /// displays the download-arrow thumbnail and derives its own membership.</summary>
        public bool IsDownloadsPlaylist => DisplayedPlaylist?.IsDownloads == true;

        /// <summary>True for either system playlist; hides editing affordances.</summary>
        public bool IsSystemPlaylist => DisplayedPlaylist?.IsSystemPlaylist == true;

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
                    DownloadAllCommand.RaiseCanExecuteChanged();
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
            IAlbumArtService albumArtService,
            IYouTubeMediaService youTubeMediaService)
        {
            _playbackViewModel = playbackViewModel ?? throw new ArgumentNullException(nameof(playbackViewModel));
            _openFileDialogService = openFileDialogService ?? throw new ArgumentNullException(nameof(openFileDialogService));
            _appDatabase = appDatabase ?? throw new ArgumentNullException(nameof(appDatabase));
            _playlistStore = playlistStore ?? throw new ArgumentNullException(nameof(playlistStore));
            _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            _albumArtService = albumArtService ?? throw new ArgumentNullException(nameof(albumArtService));
            _youTubeMediaService = youTubeMediaService ?? throw new ArgumentNullException(nameof(youTubeMediaService));

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
            DownloadAllCommand = new RelayCommand(
                ToggleDownloadAll,
                () => !IsEditMode
                    && (IsPlaylistDownloading || HasPendingDownloads || HasRemovableDownloads));
            DownloadSongCommand = new AsyncRelayCommand<Song>(
                DownloadSongAsync,
                song => song is not null && song.CurrentDownloadState == DownloadState.NotDownloaded,
                exception => _notifications.ShowError(exception.Message));
            OpenSongFolderCommand = new AsyncRelayCommand<Song>(
                ExecuteOpenSongFolderAsync,
                song => song is { FilePath.Length: > 0 },
                exception => _notifications.ShowError(exception.Message));
            RemoveDownloadCommand = new RelayCommand<Song>(
                RemoveDownload,
                song => song is not null && IsRemovableDownload(song));
            ToggleLikeCommand = new RelayCommand<Song>(ToggleLike, song => song is not null);

            _playlistStore.LikedSongsPlaylist.Songs.CollectionChanged += OnLikedSongsChanged;
        }

        public AsyncRelayCommand<Song> DownloadSongCommand { get; }

        public AsyncRelayCommand<Song> OpenSongFolderCommand { get; }

        public RelayCommand<Song> RemoveDownloadCommand { get; }

        public RelayCommand<Song> ToggleLikeCommand { get; }

        /// <summary>Toggles the whole-playlist download: start when idle, cancel while
        /// running. Downloaded playlists have nothing pending, so the click is a no-op.</summary>
        public RelayCommand DownloadAllCommand { get; }

        /// <summary>Shown only when the playlist has tracks worth a download button.</summary>
        public bool ShowDownloadButton
        {
            get => _showDownloadButton;
            private set => SetProperty(ref _showDownloadButton, value);
        }

        /// <summary>Overall download progress (0-100) while a batch is running.</summary>
        public double DownloadAllProgress
        {
            get => _downloadAllProgress;
            private set
            {
                if (SetProperty(ref _downloadAllProgress, value))
                {
                    OnPropertyChanged(nameof(DownloadButtonLabel));
                }
            }
        }

        public bool IsPlaylistNotDownloaded => _playlistDownloadState == DownloadState.NotDownloaded;

        public bool IsPlaylistDownloading => _playlistDownloadState == DownloadState.Downloading;

        public bool IsPlaylistDownloaded => _playlistDownloadState == DownloadState.Downloaded;

        public string DownloadButtonLabel => _playlistDownloadState switch
        {
            DownloadState.Downloading => $"{(int)DownloadAllProgress}%",
            DownloadState.Downloaded => "Downloaded",
            _ => "Download"
        };

        public string DownloadButtonTooltip => _playlistDownloadState switch
        {
            DownloadState.Downloading => "Cancel download",
            DownloadState.Downloaded => "Remove download",
            _ => "Download all songs"
        };

        private bool HasPendingDownloads =>
            DisplayedPlaylist?.Songs.Any(IsDownloadable) ?? false;

        private bool HasRemovableDownloads =>
            DisplayedPlaylist?.Songs.Any(IsRemovableDownload) ?? false;

        // A song is offline if we downloaded it or it already has a usable local file.
        private static bool IsOffline(Song song) =>
            song.CurrentDownloadState == DownloadState.Downloaded
            || (!string.IsNullOrWhiteSpace(song.FilePath) && File.Exists(song.FilePath));

        // Downloadable if it isn't already offline and we can source audio for it:
        // a YouTube video id, or a title we can resolve to one (like recommendations).
        private static bool IsDownloadable(Song song) =>
            song.CurrentDownloadState != DownloadState.Downloading
            && !IsOffline(song)
            && (!string.IsNullOrWhiteSpace(song.VideoId) || !string.IsNullOrWhiteSpace(song.Title));

        // Only YouTube-backed downloads can be removed: deleting the file still leaves
        // the song playable via streaming. Local files the user added are never touched.
        private static bool IsRemovableDownload(Song song) =>
            !string.IsNullOrWhiteSpace(song.VideoId)
            && !string.IsNullOrWhiteSpace(song.FilePath)
            && File.Exists(song.FilePath);

        private void ToggleDownloadAll()
        {
            if (_downloadCts is not null)
            {
                _downloadCts.Cancel();
                return;
            }

            if (HasPendingDownloads)
            {
                _ = DownloadAllAsync();
            }
            else if (HasRemovableDownloads)
            {
                RemoveAllDownloads();
            }
        }

        private void RemoveAllDownloads()
        {
            Playlist? playlist = DisplayedPlaylist;
            if (playlist is null)
            {
                return;
            }

            int removed = 0;
            foreach (Song song in playlist.Songs.ToList())
            {
                if (!IsRemovableDownload(song))
                {
                    continue;
                }

                try
                {
                    if (File.Exists(song.FilePath!))
                    {
                        File.Delete(song.FilePath!);
                    }
                }
                catch
                {
                    // The file is likely in use (e.g. currently playing); leave it
                    // downloaded rather than half-removing it.
                    continue;
                }

                song.FilePath = null;
                song.CurrentDownloadState = DownloadState.NotDownloaded;
                song.DownloadProgress = 0;
                _playlistStore.MarkUndownloaded(song);
                removed++;
            }

            RefreshDownloadState();
            if (removed > 0)
            {
                _notifications.ShowInfo($"Removed downloads for “{playlist.Name}”.");
            }
        }

        // Per-row download: resolves a video for artist+title-only songs, then fetches
        // it and persists offline — the same pipeline the whole-playlist download uses.
        private async Task DownloadSongAsync(Song? song)
        {
            if (song is null || string.IsNullOrWhiteSpace(song.Title))
            {
                return;
            }

            if (IsOffline(song))
            {
                song.CurrentDownloadState = DownloadState.Downloaded;
                song.DownloadProgress = 100;
                _playlistStore.MarkDownloaded(song);
                return;
            }

            song.CurrentDownloadState = DownloadState.Downloading;
            song.DownloadProgress = 0;
            DownloadSongCommand.RaiseCanExecuteChanged();
            var progress = new Progress<double>(value =>
                song.DownloadProgress = Math.Clamp(value, 0, 1) * 100);

            try
            {
                string? videoId = song.VideoId;
                if (string.IsNullOrWhiteSpace(videoId))
                {
                    videoId = await ResolveVideoIdAsync(song, CancellationToken.None);
                    if (!string.IsNullOrWhiteSpace(videoId))
                    {
                        song.VideoId = videoId;
                    }
                }

                if (string.IsNullOrWhiteSpace(videoId))
                {
                    song.CurrentDownloadState = DownloadState.NotDownloaded;
                    song.DownloadProgress = 0;
                    _notifications.ShowError($"Couldn't find “{song.Title}” to download.");
                    return;
                }

                Song downloaded = await _youTubeMediaService.DownloadAudioAsync(
                    videoId,
                    progress,
                    CancellationToken.None);

                song.FilePath = downloaded.FilePath;
                if (song.Duration <= TimeSpan.Zero && downloaded.Duration > TimeSpan.Zero)
                {
                    song.Duration = downloaded.Duration;
                }

                if (song.Year is null && downloaded.Year is int year && year > 0)
                {
                    song.Year = year;
                }

                if ((song.ArtworkData is null || song.ArtworkData.Length == 0)
                    && downloaded.ArtworkData is { Length: > 0 })
                {
                    song.ArtworkData = downloaded.ArtworkData;
                }

                song.CurrentDownloadState = DownloadState.Downloaded;
                song.DownloadProgress = 100;
                _playlistStore.MarkDownloaded(song);
                _notifications.ShowInfo($"Downloaded “{song.Title}” to your Music folder.");
            }
            catch
            {
                song.CurrentDownloadState = DownloadState.NotDownloaded;
                song.DownloadProgress = 0;
                throw;
            }
            finally
            {
                DownloadSongCommand.RaiseCanExecuteChanged();
                OpenSongFolderCommand.RaiseCanExecuteChanged();
                RefreshDownloadState();
            }
        }

        private static Task ExecuteOpenSongFolderAsync(Song? song)
        {
            string? folder = song?.FilePath is { Length: > 0 } filePath
                ? Path.GetDirectoryName(filePath)
                : null;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                throw new DirectoryNotFoundException("The download folder no longer exists.");
            }

            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            return Task.CompletedTask;
        }

        // Clicking the downloaded check on a row deletes the local file; the song
        // goes back to streaming and leaves the Downloaded Songs playlist.
        private void RemoveDownload(Song? song)
        {
            if (song is null || !IsRemovableDownload(song))
            {
                return;
            }

            try
            {
                File.Delete(song.FilePath!);
            }
            catch
            {
                // The file is likely in use (e.g. currently playing); leave it
                // downloaded rather than half-removing it.
                _notifications.ShowError($"Couldn't remove “{song.Title}” — the file may be in use.");
                return;
            }

            song.FilePath = null;
            song.CurrentDownloadState = DownloadState.NotDownloaded;
            song.DownloadProgress = 0;
            _playlistStore.MarkUndownloaded(song);
            DownloadSongCommand.RaiseCanExecuteChanged();
            RemoveDownloadCommand.RaiseCanExecuteChanged();
            RefreshDownloadState();
            _notifications.ShowInfo($"Removed download for “{song.Title}”.");
        }

        private void ToggleLike(Song? song)
        {
            if (song is null)
            {
                return;
            }

            bool nowLiked = _playlistStore.ToggleFavorite(song);
            _notifications.ShowInfo(nowLiked ? "Added to Liked Songs." : "Removed from Liked Songs.");
            // The Liked Songs collection change refreshes each row's heart.
        }

        private void OnLikedSongsChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => RefreshLikedStates();

        private void RefreshLikedStates()
        {
            foreach (PlaylistTrackRow row in _trackRows)
            {
                row.IsLiked = _playlistStore.IsFavorite(row.Song);
            }
        }

        private async Task DownloadAllAsync()
        {
            Playlist? playlist = DisplayedPlaylist;
            if (playlist is null)
            {
                return;
            }

            var pending = playlist.Songs.Where(IsDownloadable).ToList();
            if (pending.Count == 0)
            {
                return;
            }

            _downloadCts = new CancellationTokenSource();
            _downloadingPlaylist = playlist;
            CancellationToken cancellationToken = _downloadCts.Token;
            DownloadAllProgress = 0;
            RefreshDownloadState();

            int completed = 0;
            int failures = 0;
            try
            {
                foreach (Song song in pending)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsOffline(song))
                    {
                        completed++;
                        continue;
                    }

                    song.CurrentDownloadState = DownloadState.Downloading;
                    song.DownloadProgress = 0;
                    int baseIndex = completed;
                    var progress = new Progress<double>(value =>
                    {
                        double fraction = Math.Clamp(value, 0, 1);
                        song.DownloadProgress = fraction * 100;
                        DownloadAllProgress = (baseIndex + fraction) / pending.Count * 100;
                    });

                    try
                    {
                        // Recommendations carry only artist+title; resolve a YouTube
                        // video for them first, exactly like playback does.
                        string? videoId = song.VideoId;
                        if (string.IsNullOrWhiteSpace(videoId))
                        {
                            videoId = await ResolveVideoIdAsync(song, cancellationToken);
                            if (!string.IsNullOrWhiteSpace(videoId))
                            {
                                song.VideoId = videoId;
                            }
                        }

                        if (string.IsNullOrWhiteSpace(videoId))
                        {
                            song.CurrentDownloadState = DownloadState.NotDownloaded;
                            song.DownloadProgress = 0;
                            failures++;
                            completed++;
                            DownloadAllProgress = (double)completed / pending.Count * 100;
                            RefreshDownloadState();
                            continue;
                        }

                        Song downloaded = await _youTubeMediaService.DownloadAudioAsync(
                            videoId,
                            progress,
                            cancellationToken);

                        // Keep the playlist's own metadata; only add what makes it play
                        // offline and fill in anything it was missing.
                        song.FilePath = downloaded.FilePath;
                        if (song.Duration <= TimeSpan.Zero && downloaded.Duration > TimeSpan.Zero)
                        {
                            song.Duration = downloaded.Duration;
                        }

                        if (song.Year is null && downloaded.Year is int year && year > 0)
                        {
                            song.Year = year;
                        }

                        if ((song.ArtworkData is null || song.ArtworkData.Length == 0)
                            && downloaded.ArtworkData is { Length: > 0 })
                        {
                            song.ArtworkData = downloaded.ArtworkData;
                        }

                        song.CurrentDownloadState = DownloadState.Downloaded;
                        song.DownloadProgress = 100;
                        _playlistStore.MarkDownloaded(song);
                    }
                    catch (OperationCanceledException)
                    {
                        song.CurrentDownloadState = DownloadState.NotDownloaded;
                        song.DownloadProgress = 0;
                        throw;
                    }
                    catch
                    {
                        song.CurrentDownloadState = DownloadState.NotDownloaded;
                        song.DownloadProgress = 0;
                        failures++;
                    }

                    completed++;
                    DownloadAllProgress = (double)completed / pending.Count * 100;
                    RefreshDownloadState();
                }

                _notifications.ShowInfo(failures == 0
                    ? $"Downloaded “{playlist.Name}” for offline listening."
                    : $"Downloaded {pending.Count - failures} of {pending.Count} songs; {failures} failed.");
            }
            catch (OperationCanceledException)
            {
                _notifications.ShowInfo("Download cancelled.");
            }
            catch (Exception exception)
            {
                _notifications.ShowError(exception.Message);
            }
            finally
            {
                _downloadCts?.Dispose();
                _downloadCts = null;
                _downloadingPlaylist = null;
                DownloadAllProgress = 0;
                RefreshDownloadState();
            }
        }

        private async Task<string?> ResolveVideoIdAsync(Song song, CancellationToken cancellationToken)
        {
            string query = $"{song.Artist} {song.Title}".Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            var matches = await _youTubeMediaService.SearchAsync(
                query,
                1,
                searchSongs: true,
                cancellationToken);
            return matches.Count > 0 ? matches[0].VideoId : null;
        }

        private void RefreshDownloadState()
        {
            // Per-row download/remove buttons snapshot CanExecute when bound; every
            // state change must re-query them or a fresh check stays disabled and
            // clicks fall through to the row (which auto-plays).
            DownloadSongCommand?.RaiseCanExecuteChanged();
            RemoveDownloadCommand?.RaiseCanExecuteChanged();
            OpenSongFolderCommand?.RaiseCanExecuteChanged();

            ObservableCollection<Song>? songs = DisplayedPlaylist?.Songs;
            if (songs is null || songs.Count == 0)
            {
                ShowDownloadButton = false;
                SetPlaylistDownloadState(DownloadState.NotDownloaded);
                DownloadAllCommand.RaiseCanExecuteChanged();
                return;
            }

            bool downloadingThis = _downloadCts is not null
                && ReferenceEquals(_downloadingPlaylist, DisplayedPlaylist);
            bool anyDownloading = downloadingThis
                || songs.Any(song => song.CurrentDownloadState == DownloadState.Downloading);
            bool anyDownloadable = songs.Any(IsDownloadable);
            bool anyOffline = songs.Any(IsOffline);

            // Only worth a button when there's something to download or already offline;
            // a playlist of un-downloadable, non-offline songs shows nothing.
            ShowDownloadButton = anyDownloading || anyDownloadable || anyOffline;
            if (!ShowDownloadButton)
            {
                SetPlaylistDownloadState(DownloadState.NotDownloaded);
                DownloadAllCommand.RaiseCanExecuteChanged();
                return;
            }

            DownloadState next;
            if (anyDownloading)
            {
                next = DownloadState.Downloading;
            }
            else if (anyDownloadable)
            {
                next = DownloadState.NotDownloaded;
            }
            else
            {
                next = DownloadState.Downloaded;
            }

            SetPlaylistDownloadState(next);
            DownloadAllCommand.RaiseCanExecuteChanged();
        }

        private void SetPlaylistDownloadState(DownloadState state)
        {
            if (_playlistDownloadState == state)
            {
                return;
            }

            _playlistDownloadState = state;
            OnPropertyChanged(nameof(IsPlaylistNotDownloaded));
            OnPropertyChanged(nameof(IsPlaylistDownloading));
            OnPropertyChanged(nameof(IsPlaylistDownloaded));
            OnPropertyChanged(nameof(DownloadButtonLabel));
            OnPropertyChanged(nameof(DownloadButtonTooltip));
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
            RefreshDownloadState();
            PlayPlaylistCommand.RaiseCanExecuteChanged();
            ShufflePlaylistCommand.RaiseCanExecuteChanged();
            _ = EnsureThumbnailsAsync();
        }

        private void OnSongPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(Song.CurrentDownloadState) or nameof(Song.FilePath))
            {
                RefreshDownloadState();
            }

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
                _trackRows.Add(new PlaylistTrackRow(i + 1, song, isPlaying)
                {
                    IsLiked = _playlistStore.IsFavorite(song)
                });
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
            _playlistStore.LikedSongsPlaylist.Songs.CollectionChanged -= OnLikedSongsChanged;
            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _downloadCts = null;

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
