using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SoundHaven.Commands;
using SoundHaven.Models;
using SoundHaven.Services;

namespace SoundHaven.ViewModels;

public sealed class SearchViewModel : ViewModelBase
{
    private readonly IYouTubeMediaService _youTubeMediaService;
    private readonly PlaybackViewModel _playbackViewModel;
    private readonly IUserNotificationService _notifications;
    private CancellationTokenSource _searchCancellation = new();
    private CancellationTokenSource _lifetimeCancellation = new();
    private string _searchQuery = string.Empty;
    private bool _isLoading;
    private string _loadingMessage = string.Empty;
    private Song? _selectedSong;
    private bool _isScrollViewerHittestable = true;
    private bool _toggleSearchResults = true;

    public SearchViewModel(
        IYouTubeMediaService youTubeMediaService,
        PlaybackViewModel playbackViewModel,
        IUserNotificationService notifications)
    {
        _youTubeMediaService = youTubeMediaService
            ?? throw new ArgumentNullException(nameof(youTubeMediaService));
        _playbackViewModel = playbackViewModel
            ?? throw new ArgumentNullException(nameof(playbackViewModel));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

        SearchResults = [];
        SearchCommand = new AsyncRelayCommand(ExecuteSearchAsync, onException: ShowFailure);
        PlaySongCommand = new AsyncRelayCommand<Song>(
            ExecutePlaySongAsync,
            song => song is not null,
            ShowFailure);
        DownloadSongCommand = new AsyncRelayCommand<Song>(
            ExecuteDownloadSongAsync,
            song => song is not null && song.CurrentDownloadState == DownloadState.NotDownloaded,
            ShowFailure);
        OpenFolderCommand = new AsyncRelayCommand<Song>(
            ExecuteOpenFolderAsync,
            song => !string.IsNullOrWhiteSpace(song?.FilePath),
            ShowFailure);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    public ObservableCollection<Song> SearchResults { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string LoadingMessage
    {
        get => _loadingMessage;
        private set => SetProperty(ref _loadingMessage, value);
    }

    public Song? SelectedSong
    {
        get => _selectedSong;
        set => SetProperty(ref _selectedSong, value);
    }

    public bool IsScrollViewerHittestable
    {
        get => _isScrollViewerHittestable;
        private set => SetProperty(ref _isScrollViewerHittestable, value);
    }

    public bool ToggleSearchResults
    {
        get => _toggleSearchResults;
        set
        {
            if (SetProperty(ref _toggleSearchResults, value))
            {
                OnPropertyChanged(nameof(SearchButtonText));
            }
        }
    }

    public string SearchButtonText => ToggleSearchResults ? "Search Songs" : "Search Videos";

    public AsyncRelayCommand SearchCommand { get; }

    public AsyncRelayCommand<Song> PlaySongCommand { get; }

    public AsyncRelayCommand<Song> DownloadSongCommand { get; }

    public AsyncRelayCommand<Song> OpenFolderCommand { get; }

    public override void Dispose()
    {
        _searchCancellation.Cancel();
        _searchCancellation.Dispose();
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        base.Dispose();
    }

    private async Task ExecuteSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        var nextCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        CancellationTokenSource previousCancellation = Interlocked.Exchange(
            ref _searchCancellation,
            nextCancellation);
        previousCancellation.Cancel();
        previousCancellation.Dispose();

        IsLoading = true;
        LoadingMessage = ToggleSearchResults ? "Searching for songs..." : "Searching for videos...";
        SearchResults.Clear();
        try
        {
            var results = await _youTubeMediaService.SearchAsync(
                SearchQuery,
                15,
                ToggleSearchResults,
                nextCancellation.Token);

            foreach (YouTubeSearchResult result in results)
            {
                nextCancellation.Token.ThrowIfCancellationRequested();
                SearchResults.Add(new Song
                {
                    Title = result.Title,
                    Artist = result.Author,
                    Album = result.Album,
                    VideoId = result.VideoId,
                    ThumbnailUrl = result.ThumbnailUrl,
                    ChannelTitle = result.Author,
                    Duration = result.Duration ?? TimeSpan.Zero,
                    VideoDuration = FormatDuration(result.Duration),
                    Views = FormatViewCount(result.ViewCount),
                    Year = result.Year
                });
            }
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, nextCancellation))
            {
                IsLoading = false;
                LoadingMessage = string.Empty;
            }
        }
    }

    private async Task ExecutePlaySongAsync(Song? song)
    {
        if (song is null)
        {
            return;
        }

        IsScrollViewerHittestable = false;
        try
        {
            var playlist = new Playlist { Name = "Streaming from YouTube" };
            _playbackViewModel.CurrentPlaylist = playlist;
            await _playbackViewModel.AddToUpNext(song);
            await _playbackViewModel.PlayFromBeginning(song);
        }
        finally
        {
            IsScrollViewerHittestable = true;
        }
    }

    private async Task ExecuteDownloadSongAsync(Song? song)
    {
        if (song?.VideoId is null)
        {
            return;
        }

        song.CurrentDownloadState = DownloadState.Downloading;
        song.DownloadProgress = 0;
        DownloadSongCommand.RaiseCanExecuteChanged();

        var progress = new Progress<double>(value =>
        {
            song.DownloadProgress = Math.Clamp(value, 0, 1) * 100;
        });

        try
        {
            Song downloadedSong = await _youTubeMediaService.DownloadAudioAsync(
                song.VideoId,
                progress,
                _lifetimeCancellation.Token);

            song.FilePath = downloadedSong.FilePath;
            song.Title = downloadedSong.Title;
            song.Artist = downloadedSong.Artist;
            song.Album = downloadedSong.Album;
            song.Duration = downloadedSong.Duration;
            song.Year = downloadedSong.Year;
            song.ArtworkData = downloadedSong.ArtworkData;
            song.CurrentDownloadState = DownloadState.Downloaded;
            song.DownloadProgress = 100;
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
            OpenFolderCommand.RaiseCanExecuteChanged();
        }
    }

    private static Task ExecuteOpenFolderAsync(Song? song)
    {
        string? folder = song?.FilePath is { Length: > 0 } filePath
            ? Path.GetDirectoryName(filePath)
            : null;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException("The download folder no longer exists.");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
        return Task.CompletedTask;
    }

    private static string FormatDuration(TimeSpan? duration)
    {
        if (duration is null)
        {
            return "0:00";
        }

        return duration.Value.TotalHours >= 1
            ? duration.Value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.Value.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private static string FormatViewCount(long viewCount)
    {
        return viewCount switch
        {
            < 1_000 => $"{viewCount} views",
            < 1_000_000 => $"{viewCount / 1_000d:0.#}K views",
            < 1_000_000_000 => $"{viewCount / 1_000_000d:0.#}M views",
            _ => $"{viewCount / 1_000_000_000d:0.#}B views"
        };
    }

    private void ShowFailure(Exception exception)
    {
        _notifications.ShowError(exception.Message);
    }
}
