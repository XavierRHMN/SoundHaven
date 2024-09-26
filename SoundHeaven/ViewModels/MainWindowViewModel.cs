using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
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
                    AudioService.Start(_currentSong.FilePath);
                    VolumeControlViewModel.Volume = AudioService.GetCurrentVolume();
                    VolumeControlViewModel.MuteCommand.RaiseCanExecuteChanged();
                    PlaybackControlViewModel.IsPlaying = true;

                    // Update text width based on the new song title
                    TextWidth = ExtractTextWidth(CurrentSong?.Title, "Nunito", 15);
                    
                    // Initialize scroll positions
                    InitializeScrollPositions();
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

        // For the scrolling text
        private double _titleScrollPosition1;
        public double TitleScrollPosition1
        {
            get => _titleScrollPosition1;
            set
            {
                _titleScrollPosition1 = value;
                OnPropertyChanged();
            }
        }

        private double _titleScrollPosition2;
        public double TitleScrollPosition2
        {
            get => _titleScrollPosition2;
            set
            {
                _titleScrollPosition2 = value;
                OnPropertyChanged();
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
        
        
        private double _textWidth;
        public double TextWidth
        {
            get => _textWidth;
            set
            {
                _textWidth = value;
                OnPropertyChanged();
            }
        }
        
        // ViewModels
        public PlaylistViewModel PlaylistViewModel { get; set; }
        public HomeViewModel HomeViewModel { get; set; }
        public ToolBarControlViewModel ToolBarControlViewModel { get; set; }
        public PlaybackControlViewModel PlaybackControlViewModel { get; set; }
        public ShuffleControlViewModel ShuffleControlViewModel { get; }
        public PlayerViewModel PlayerViewModel { get; set; }
        public SeekSliderControlViewModel SeekSliderControlViewModel { get; set; }
        public VolumeControlViewModel VolumeControlViewModel { get; }
        

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
            
            ShuffleControlViewModel = new ShuffleControlViewModel(this);
            PlaybackControlViewModel = new PlaybackControlViewModel(this, AudioService);
            PlaylistViewModel = new PlaylistViewModel(this, new OpenFileDialogService());
            PlayerViewModel = new PlayerViewModel(this);
            HomeViewModel = new HomeViewModel(this, dataService);
            ToolBarControlViewModel = new ToolBarControlViewModel(this, PlaylistViewModel, HomeViewModel, PlayerViewModel, PlaylistStore);
            SeekSliderControlViewModel = new SeekSliderControlViewModel(this, AudioService, PlaybackControlViewModel);
            VolumeControlViewModel = new VolumeControlViewModel(AudioService);
            
            // Make sure to set the initial state of shuffle
            PlaybackControlViewModel.IsShuffleEnabled = ShuffleControlViewModel.IsShuffleEnabled;
            
            // Set initial CurrentViewModel
            CurrentViewModel = HomeViewModel;

            InitializeExamplePlaylist();
            InitializeScrollPositions();
            InitializeScrollTimer();
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
        
        private void InitializeScrollTimer()
        {
            _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _scrollTimer.Tick += (sender, e) => ScrollText();
            _scrollTimer.Start();
        }

        public double ControlWidth { get; set; } = 200 * 2; // Width of the canvas/border

        private void InitializeScrollPositions()
        {
            // Initialize the scroll positions based on the text width
            // Assuming the first TextBlock starts at 0 and the second starts after the first one with some spacing
            const double spacing = 50; // Adjust spacing as needed
            const double spaceFromLeft = 200;
            
            TitleScrollPosition1 = spaceFromLeft;
            TitleScrollPosition2 =  spaceFromLeft + TextWidth + spacing;
        }
        
        
        private void ScrollText()
        {
            
            const double scrollSpeed = 2; // Adjust scroll speed as needed
            const double spacing = 50; // Spacing between the two TextBlocks

            // Update both scroll positions
            TitleScrollPosition1 -= scrollSpeed;
            TitleScrollPosition2 -= scrollSpeed;

            // Reset the first TextBlock if it's completely out of view
            if (TitleScrollPosition1 <= -TextWidth)
            {
                TitleScrollPosition1 = TitleScrollPosition2 + TextWidth + spacing;
            }

            // Reset the second TextBlock if it's completely out of view
            if (TitleScrollPosition2 <= -TextWidth)
            {
                TitleScrollPosition2 = TitleScrollPosition1 + TextWidth + spacing;
            }

            // Notify property changes
            OnPropertyChanged(nameof(TitleScrollPosition1));
            OnPropertyChanged(nameof(TitleScrollPosition2));
        }

        private double ExtractTextWidth(string text, string fontFamily, double fontSize)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            var typeface = new Typeface(fontFamily);
            var formattedText = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.Black
            );

            return formattedText.Width;
        }
    }
}
