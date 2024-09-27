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

        public bool CurrentSongExists => CurrentSong != null;
        private Song _currentSong;
        public Song CurrentSong
        {
            get => _currentSong;
            set
            {
                if (_currentSong != value)
                {
                    _currentSong = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentSongExists));
                    SongInfoViewModel.CurrentSong = value;
                    AudioService.Start(_currentSong.FilePath);
                    PlaybackViewModel.IsPlaying = true;
                }
            }
        }

        private Playlist? _currentPlaylist;
        public Playlist? CurrentPlaylist
        {
            get => _currentPlaylist;
            set
            {
                if (_currentPlaylist != value)
                {
                    _currentPlaylist = value;
                    OnPropertyChanged();
                }
            }
        }

        private ViewModelBase _currentViewModel;
        public ViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            set
            {
                if (_currentViewModel != value) // Check if the new value is different
                {
                    _currentViewModel = value;
                    OnPropertyChanged(nameof(CurrentViewModel)); // Notify the UI of the property change
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
            
            AudioService = new AudioService();
            PlaylistStore = new PlaylistStore(this);
            SongStore = new SongStore();
            
            ShuffleViewModel = new ShuffleViewModel(this);
            PlaybackViewModel = new PlaybackViewModel(this, AudioService);
            PlaylistViewModel = new PlaylistViewModel(this, new OpenFileDialogService());
            PlayerViewModel = new PlayerViewModel(this);
            HomeViewModel = new HomeViewModel(this, dataService);
            ThemesViewModel = new ThemesViewModel(this);
            ToolbarViewModel = new ToolbarViewModel(this, PlaylistViewModel, HomeViewModel, PlayerViewModel, PlaylistStore, ThemesViewModel);
            SeekSliderViewModel = new SeekSliderViewModel(this, AudioService, PlaybackViewModel);
            VolumeViewModel = new VolumeViewModel(AudioService);
            SongInfoViewModel = new SongInfoViewModel();
            RepeatViewModel = new RepeatViewModel();
            
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
        }
    }
}
