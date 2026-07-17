using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Caching.Memory;
using SoundHaven.Data;
using SoundHaven.Helpers;
using SoundHaven.Services;
using SoundHaven.Stores;
using SoundHaven.ViewModels;

namespace SoundHaven;

public partial class App : Application, IDisposable
{
    private MainWindowViewModel? _mainViewModel;
    private AudioService? _audioService;
    private YouTubeMediaService? _youTubeMediaService;
    private LastFmDataService? _lastFmDataService;
    private MemoryCache? _memoryCache;
    private HttpClient? _httpClient;
    private bool _disposed;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (BindingPlugins.DataValidators.Count > 0)
            {
                BindingPlugins.DataValidators.RemoveAt(0);
            }

            _mainViewModel = BuildCompositionRoot();
            desktop.MainWindow = new MainWindow
            {
                DataContext = _mainViewModel
            };
            desktop.Exit += (_, _) => Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private MainWindowViewModel BuildCompositionRoot()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
            + "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("image/*"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("*/*"));

        var notifications = new NotificationService();
        var database = new AppDatabase();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _lastFmDataService = new LastFmDataService(
            ApiKeyHelper.GetApiKey(),
            ApiKeyHelper.GetApiSecret(),
            _httpClient,
            _memoryCache);
        _youTubeMediaService = new YouTubeMediaService(_httpClient);
        _audioService = new AudioService(_youTubeMediaService);

        var recentPlaybackStore = new RecentPlaybackStore();
        var repeatViewModel = new RepeatViewModel();
        var themesViewModel = new ThemesViewModel(database);
        var playbackViewModel = new PlaybackViewModel(
            _audioService,
            repeatViewModel,
            _lastFmDataService,
            themesViewModel,
            notifications,
            recentPlaybackStore);
        var shuffleViewModel = new ShuffleViewModel(playbackViewModel);
        var playlistViewModel = new PlaylistViewModel(
            playbackViewModel,
            new OpenFileDialogService(),
            database,
            notifications);
        var playlistStore = new PlaylistStore(database);
        var playerViewModel = new PlayerViewModel(playbackViewModel, playlistStore, notifications);
        var lastFmViewModel = new LastFmViewModel(_lastFmDataService);
        var seekSliderViewModel = new SeekSliderViewModel(
            _audioService,
            playbackViewModel,
            notifications);
        var searchViewModel = new SearchViewModel(
            _youTubeMediaService,
            playbackViewModel,
            playlistStore,
            notifications);
        var volumeViewModel = new VolumeViewModel(_audioService);
        var songInfoViewModel = new SongInfoViewModel(playbackViewModel, _audioService);
        var navigation = new NavigationService(new ViewModelBase());
        var homeViewModel = new HomeViewModel(
            playlistStore,
            recentPlaybackStore,
            playbackViewModel,
            playlistViewModel,
            navigation,
            notifications,
            _lastFmDataService,
            _youTubeMediaService);
        navigation.NavigateTo(homeViewModel);
        var toolbarViewModel = new ToolbarViewModel(
            navigation,
            playlistViewModel,
            playbackViewModel,
            homeViewModel,
            lastFmViewModel,
            playerViewModel,
            playlistStore,
            searchViewModel,
            themesViewModel,
            notifications);

        return new MainWindowViewModel(
            navigation,
            playlistViewModel,
            homeViewModel,
            lastFmViewModel,
            toolbarViewModel,
            playbackViewModel,
            shuffleViewModel,
            playerViewModel,
            seekSliderViewModel,
            volumeViewModel,
            songInfoViewModel,
            themesViewModel,
            repeatViewModel,
            searchViewModel,
            notifications);
    }

    private void DisposeCompositionRoot()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mainViewModel?.Dispose();
        _audioService?.Dispose();
        _youTubeMediaService?.Dispose();
        _lastFmDataService?.Dispose();
        _memoryCache?.Dispose();
        _httpClient?.Dispose();
    }

    public void Dispose()
    {
        DisposeCompositionRoot();
        GC.SuppressFinalize(this);
    }
}
