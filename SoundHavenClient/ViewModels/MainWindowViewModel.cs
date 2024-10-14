using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SoundHaven.Helpers;
using SoundHaven.Services;
using SoundHaven.Stores;
using SoundHaven.Data;
using System;
using System.IO;

namespace SoundHaven.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly AppDatabase AppDatabase;
        private AudioService AudioService { get; set; }
        private PlaylistStore PlaylistStore { get; set; }

        private ViewModelBase _currentViewModel;
        public ViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            set => SetProperty(ref _currentViewModel, value);
        }

        // ViewModels
        public PlaylistViewModel PlaylistViewModel { get; set; }
        public HomeViewModel HomeViewModel { get; set; }
        public ToolbarViewModel ToolbarViewModel { get; set; }
        public PlaybackViewModel PlaybackViewModel { get; set; }
        public ShuffleViewModel ShuffleViewModel { get; }
        public PlayerViewModel PlayerViewModel { get; set; }
        public SeekSliderViewModel SeekSliderViewModel { get; set; }
        public VolumeViewModel VolumeViewModel { get; }
        public SongInfoViewModel SongInfoViewModel { get; set; }
        public ThemesViewModel ThemesViewModel { get; set; }
        public RepeatViewModel RepeatViewModel { get; set; }
        public SearchViewModel SearchViewModel { get; set; }
        public IYouTubeDownloadService YouTubeDownloadService { get; set; }
        public IYouTubeSearchService YoutubeSearchService { get; set; }

        public MainWindowViewModel()
        {
            // SQLite Database
            string dbPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Data", "AppDatabase.db");
            AppDatabase = new AppDatabase(dbPath);

            // LastFM Song Caching and LastFM Api Key provider
            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var apiKeyProvider = new ApiKeyService();

            string lastFmApiKey = apiKeyProvider.GetApiKey("LASTFM_API_KEY.txt");
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var lastFmDataService = new LastFmDataService(lastFmApiKey, memoryCache, loggerFactory);
            var youtubeLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

            // Services
            AudioService = new AudioService();
            YouTubeDownloadService = new YouTubeDownloadService();
            YoutubeSearchService = new YouTubeSearchService();

            // Stores
            PlaylistStore = new PlaylistStore(AppDatabase);

            // ViewModels
            RepeatViewModel = new RepeatViewModel();
            PlaybackViewModel = new PlaybackViewModel(AudioService, YouTubeDownloadService, RepeatViewModel);
            ShuffleViewModel = new ShuffleViewModel(PlaybackViewModel);
            PlaylistViewModel = new PlaylistViewModel(PlaybackViewModel, new OpenFileDialogService(), AppDatabase);
            PlayerViewModel = new PlayerViewModel(PlaybackViewModel);
            HomeViewModel = new HomeViewModel(PlaybackViewModel, lastFmDataService);
            ThemesViewModel = new ThemesViewModel(AppDatabase);
            SeekSliderViewModel = new SeekSliderViewModel(AudioService, PlaybackViewModel);
            SearchViewModel = new SearchViewModel(YoutubeSearchService, YouTubeDownloadService, new OpenFileDialogService(), AudioService, PlaybackViewModel, new MpvDownloader(), SeekSliderViewModel);
            ToolbarViewModel = new ToolbarViewModel(this, PlaylistViewModel, HomeViewModel, PlayerViewModel, PlaylistStore, SearchViewModel, ThemesViewModel);
            VolumeViewModel = new VolumeViewModel(AudioService);
            SongInfoViewModel = new SongInfoViewModel(PlaybackViewModel, AudioService);

            CurrentViewModel = HomeViewModel;
        }
    }
}
