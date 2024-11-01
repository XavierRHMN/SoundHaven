using Avalonia.Threading;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using SoundHaven.Commands;
using SoundHaven.Models;
using SoundHaven.Services;
using SoundHaven.Helpers;
using System.Collections.Generic;
using YoutubeExplode.Videos;

namespace SoundHaven.ViewModels
{
    public class SearchViewModel : ViewModelBase
    {
        private readonly FileDownloader _fileDownloader;
        private readonly IYouTubeSearchService _youtubeSearchService;
        private readonly IYouTubeDownloadService _youTubeDownloadService;
        private readonly IOpenFileDialogService _openFileDialogService;
        private readonly AudioService _audioService;
        private readonly PlaybackViewModel _playbackViewModel;
        private readonly SeekSliderViewModel _seekSliderViewModel;

        private string _searchQuery;
        private ObservableCollection<Song> _searchResults;
        private bool _isLoading;
        private string _loadingMessage;
        private Song _selectedSong;
        private bool _isMpvLoading;
        private bool _isScrollViewerHittestable = true;
        private bool _toggleSearchResults = true;

        public string SearchQuery
        {
            get => _searchQuery;
            set => SetProperty(ref _searchQuery, value);
        }

        public ObservableCollection<Song> SearchResults
        {
            get => _searchResults;
            set => SetProperty(ref _searchResults, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string LoadingMessage
        {
            get => _loadingMessage;
            set => SetProperty(ref _loadingMessage, value);
        }

        public Song SelectedSong
        {
            get => _selectedSong;
            set
            {
                if (SetProperty(ref _selectedSong, value) && value != null)
                {
                    _ = PlaySongCommand.ExecuteAsync(value);
                }
            }
        }

        public bool IsMpvLoading
        {
            get => _isMpvLoading;
            set => SetProperty(ref _isMpvLoading, value);
        }

        public bool IsScrollViewerHittestable
        {
            get => _isScrollViewerHittestable;
            set => SetProperty(ref _isScrollViewerHittestable, value);
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

        public SearchViewModel(
            IYouTubeSearchService youtubeSearchService,
            IYouTubeDownloadService youTubeDownloadService,
            IOpenFileDialogService openFileDialogService,
            AudioService audioService,
            PlaybackViewModel playbackViewModel,
            FileDownloader fileDownloader,
            SeekSliderViewModel seekSliderViewModel)
        {
            _youtubeSearchService = youtubeSearchService;
            _youTubeDownloadService = youTubeDownloadService;
            _openFileDialogService = openFileDialogService;
            _audioService = audioService;
            _playbackViewModel = playbackViewModel;
            _fileDownloader = fileDownloader;
            _seekSliderViewModel = seekSliderViewModel;

            SearchResults = new ObservableCollection<Song>();

            // Initialize commands
            SearchCommand = new AsyncRelayCommand(ExecuteSearchAsync);
            PlaySongCommand = new AsyncRelayCommand<Song>(ExecutePlaySongAsync);
            DownloadSongCommand = new AsyncRelayCommand<Song>(ExecuteDownloadSongAsync);
            OpenFolderCommand = new AsyncRelayCommand<Song>(ExecuteOpenFolderAsync);

            // Start MPV initialization in the background
            _ = InitializeMpvAsync();
        }

        private async Task InitializeMpvAsync()
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsMpvLoading = true;
                    LoadingMessage = "Initializing MPV...";
                });

                await Task.Run(async () => await _fileDownloader.DownloadAndUpdateFilesAsync());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing MPV: {ex.Message}");
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsMpvLoading = false;
                    LoadingMessage = string.Empty;
                });
            }
        }

        private async Task ExecuteSearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) 
                return;

            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsLoading = true;
                    SearchResults.Clear();
                    LoadingMessage = "Loading songs...";
                });

                var results = await Task.Run(async () => 
                    await _youtubeSearchService.SearchAsync(SearchQuery, 15, ToggleSearchResults));

                await AddResultsGradually(results);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during search: {ex.Message}");
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsLoading = false;
                    LoadingMessage = string.Empty;
                });
            }
        }

        private async Task AddResultsGradually(IEnumerable<VideoSearchResult> results)
        {
            foreach (var result in results)
            {
                var song = new Song
                {
                    Title = result.Title,
                    Artist = result.Author,
                    Album = result.Album,
                    VideoId = result.VideoId,
                    ThumbnailUrl = result.ThumbnailUrl,
                    ChannelTitle = result.Author,
                    VideoDuration = FormatDurationToMinutesSeconds(result.Duration),
                    Views = FormatViewCount(result.ViewCount),
                    Year = result.Year
                };

                // Load thumbnail in background
                await Task.Run(async () => await song.LoadYouTubeThumbnail());

                // Add to collection on UI thread
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SearchResults.Add(song);
                });

                // Small delay to prevent UI freezing with large results
                await Task.Delay(50);
            }
        }

        private async Task ExecutePlaySongAsync(Song song)
        {
            if (song == null) return;

            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsScrollViewerHittestable = false;
                    if (song.IsYouTubeVideo)
                    {
                        _playbackViewModel.CanPlaybackControl = false;
                        _seekSliderViewModel.CanInteractSeekSlider = false;
                    }
                });

                await Task.Run(async () =>
                {
                    var playlist = new Playlist { Name = "Streaming from YouTube" };
                    await Dispatcher.UIThread.InvokeAsync(() => 
                    {
                        _playbackViewModel.CurrentPlaylist = playlist;
                    });

                    await _playbackViewModel.AddToUpNext(song);
                    await _playbackViewModel.PlayFromBeginning(song);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing song: {ex.Message}");
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsScrollViewerHittestable = true;
                    _playbackViewModel.CanPlaybackControl = true;
                    _seekSliderViewModel.CanInteractSeekSlider = true;
                });
            }
        }

        private async Task ExecuteDownloadSongAsync(Song song)
        {
            if (song == null) return;

            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    song.CurrentDownloadState = DownloadState.Downloading;
                    song.DownloadProgress = 0;
                });

                var progress = new Progress<double>(async p =>
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        song.DownloadProgress = p * 100;
                    });
                });

                string cleanVideoId = _youTubeDownloadService.CleanVideoId(song.VideoId);
                var downloadedSong = await Task.Run(async () => 
                    await _youTubeDownloadService.DownloadAudioAsync(cleanVideoId, progress));

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    song.FilePath = downloadedSong.FilePath;
                    song.Title = downloadedSong.Title;
                    song.Artist = downloadedSong.Artist;
                    song.CurrentDownloadState = DownloadState.Downloaded;
                    song.DownloadProgress = 0;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading song: {ex.Message}");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    song.CurrentDownloadState = DownloadState.NotDownloaded;
                    song.DownloadProgress = 0;
                });
            }
        }

        private async Task ExecuteOpenFolderAsync(Song song)
        {
            if (song?.FilePath == null) return;

            await Task.Run(() =>
            {
                try
                {
                    string folder = Path.GetDirectoryName(song.FilePath);
                    if (folder != null)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = folder,
                            UseShellExecute = true,
                            Verb = "open"
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error opening folder: {ex.Message}");
                }
            });
        }

        private string FormatDurationToMinutesSeconds(TimeSpan? duration)
        {
            if (!duration.HasValue)
                return "0:00";

            int hours = duration.Value.Hours;
            int minutes = duration.Value.Minutes;
            int seconds = duration.Value.Seconds;

            return hours > 0 
                ? $"{hours}:{minutes:D2}:{seconds:D2}" 
                : $"{minutes}:{seconds:D2}";
        }

        private string FormatViewCount(long viewCount)
        {
            if (viewCount < 1000)
                return $"{viewCount} views";
            else if (viewCount < 1_000_000)
                return $"{viewCount / 1000.0:F1}K views".Replace(".0K", "K");
            else
                return $"{viewCount / 1_000_000.0:F1}M views".Replace(".0M", "M");
        }
    }
}