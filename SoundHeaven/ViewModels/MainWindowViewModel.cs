using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Styles.Themes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoundHeaven.Commands;
using SoundHeaven.Converters;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SoundHeaven.Models;
using SoundHeaven.Services;
using SoundHeaven.Stores;
using SoundHeaven.Views;
using System;
using System.Globalization;
using System.Linq;
using System.Windows.Input;

namespace SoundHeaven.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public ObservableCollection<Playlist> PlaylistCollection => PlaylistStore.Playlists;

        public AudioService AudioService { get; set; }
        public PlaylistStore PlaylistStore { get; set; }
        public SongStore SongStore { get; set; }
        private DispatcherTimer _seekTimer;
        private DispatcherTimer _scrollTimer;

        private ViewModelBase _currentViewModel;
        public ViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            set
            {
                if (_currentViewModel != value)
                {
                    _currentViewModel = value;
                    OnPropertyChanged(nameof(CurrentViewModel));
                }
            }
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

        public MainWindowViewModel()
        {
            IApiKeyProvider apiKeyProvider = new ApiKeyService("API_KEY.txt");
            string apiKey = apiKeyProvider.GetApiKey();
            var memoryCacheOptions = Options.Create(new MemoryCacheOptions());
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
            });

            var memoryCache = new MemoryCache(memoryCacheOptions, loggerFactory);
            var dataService = new LastFmDataService(apiKey, memoryCache, loggerFactory);
        
            // Services
            AudioService = new AudioService();
            
            // Stores
            PlaylistStore = new PlaylistStore(this);
            SongStore = new SongStore();
            
            // ViewModels
            RepeatViewModel = new RepeatViewModel();
            PlaybackViewModel = new PlaybackViewModel(AudioService, RepeatViewModel);
            ShuffleViewModel = new ShuffleViewModel(this);
            PlaylistViewModel = new PlaylistViewModel(this, PlaybackViewModel, new OpenFileDialogService());
            PlayerViewModel = new PlayerViewModel(PlaybackViewModel);
            HomeViewModel = new HomeViewModel(PlaybackViewModel, dataService);
            ThemesViewModel = new ThemesViewModel(this);
            ToolbarViewModel = new ToolbarViewModel(this, PlaylistViewModel, HomeViewModel, PlayerViewModel, PlaylistStore, ThemesViewModel);
            SeekSliderViewModel = new SeekSliderViewModel(AudioService, PlaybackViewModel);
            VolumeViewModel = new VolumeViewModel(AudioService);
            SongInfoViewModel = new SongInfoViewModel(PlaybackViewModel);
        
            CurrentViewModel = HomeViewModel;

            InitializeExamplePlaylist();
        }

        private void InitializeExamplePlaylist()
        {
            SongStore.LoadSongs();
            
            var example = new Playlist()
            {
                Name = "Playlist #1",
                Songs = SongStore.Songs
            };
            Console.WriteLine(SongStore.Songs);
            PlaylistStore.AddPlaylist(example);
            PlaybackViewModel.CurrentPlaylist = example;
        }
    }
}