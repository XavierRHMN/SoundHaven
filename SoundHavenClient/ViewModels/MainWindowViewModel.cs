using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using SoundHaven.Commands;
using SoundHaven.Services;
using SoundHaven.Stores;

namespace SoundHaven.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly IAudioService _audioService;
    private readonly PlaylistStore _playlistStore;
    private bool _isQueueVisible;
    private bool _isCurrentSongFavorite;

    public MainWindowViewModel(
        NavigationService navigation,
        PlaylistViewModel playlistViewModel,
        HomeViewModel homeViewModel,
        ToolbarViewModel toolbarViewModel,
        PlaybackViewModel playbackViewModel,
        ShuffleViewModel shuffleViewModel,
        PlayerViewModel playerViewModel,
        SeekSliderViewModel seekSliderViewModel,
        VolumeViewModel volumeViewModel,
        SongInfoViewModel songInfoViewModel,
        RepeatViewModel repeatViewModel,
        SearchViewModel searchViewModel,
        PlaylistStore playlistStore,
        IAudioService audioService,
        NotificationService notifications)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _playlistStore = playlistStore ?? throw new ArgumentNullException(nameof(playlistStore));
        PlaylistViewModel = playlistViewModel;
        HomeViewModel = homeViewModel;
        ToolbarViewModel = toolbarViewModel;
        PlaybackViewModel = playbackViewModel;
        ShuffleViewModel = shuffleViewModel;
        PlayerViewModel = playerViewModel;
        SeekSliderViewModel = seekSliderViewModel;
        VolumeViewModel = volumeViewModel;
        SongInfoViewModel = songInfoViewModel;
        RepeatViewModel = repeatViewModel;
        SearchViewModel = searchViewModel;
        Notifications = notifications;

        ToggleQueueCommand = new RelayCommand(() => IsQueueVisible = !IsQueueVisible);
        ToggleFavoriteCommand = new RelayCommand(
            ToggleFavorite,
            () => PlaybackViewModel.CurrentSong is not null);
        _navigation.PropertyChanged += OnNavigationPropertyChanged;
        PlaybackViewModel.PropertyChanged += OnPlaybackPropertyChanged;
        _playlistStore.LikedSongsPlaylist.Songs.CollectionChanged += OnLikedSongsChanged;
        RefreshFavoriteState();
    }

    /// <summary>Whether the currently playing song is in Liked Songs; drives the
    /// player-bar heart's filled/outline state.</summary>
    public bool IsCurrentSongFavorite
    {
        get => _isCurrentSongFavorite;
        private set => SetProperty(ref _isCurrentSongFavorite, value);
    }

    public RelayCommand ToggleFavoriteCommand { get; }

    /// <summary>Docked play-queue panel on the right of the content area.</summary>
    public bool IsQueueVisible
    {
        get => _isQueueVisible;
        set => SetProperty(ref _isQueueVisible, value);
    }

    public RelayCommand ToggleQueueCommand { get; }

    /// <summary>Audio output devices for the player-bar sound-output menu.</summary>
    public IReadOnlyList<AudioOutputDevice> GetOutputDevices() => _audioService.GetOutputDevices();

    public string CurrentOutputDeviceId => _audioService.CurrentOutputDeviceId;

    public Task SetOutputDeviceAsync(string deviceId) =>
        _audioService.SetOutputDeviceAsync(deviceId);

    public ViewModelBase CurrentViewModel => _navigation.CurrentViewModel;

    // Views stay attached permanently and toggle visibility (see MainWindow.axaml);
    // swapping ContentControl content forced a full re-layout and texture re-upload
    // of the incoming view on every navigation, which made tab switches lag.
    public bool IsHomeVisible => ReferenceEquals(CurrentViewModel, HomeViewModel);

    public bool IsPlaylistVisible => ReferenceEquals(CurrentViewModel, PlaylistViewModel);

    public bool IsPlayerVisible => ReferenceEquals(CurrentViewModel, PlayerViewModel);

    public PlaylistViewModel PlaylistViewModel { get; }

    public HomeViewModel HomeViewModel { get; }

    public ToolbarViewModel ToolbarViewModel { get; }

    public PlaybackViewModel PlaybackViewModel { get; }

    public ShuffleViewModel ShuffleViewModel { get; }

    public PlayerViewModel PlayerViewModel { get; }

    public SeekSliderViewModel SeekSliderViewModel { get; }

    public VolumeViewModel VolumeViewModel { get; }

    public SongInfoViewModel SongInfoViewModel { get; }

    public RepeatViewModel RepeatViewModel { get; }

    public SearchViewModel SearchViewModel { get; }

    public NotificationService Notifications { get; }

    private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(PlaybackViewModel.CurrentSong))
        {
            RefreshFavoriteState();
            ToggleFavoriteCommand.RaiseCanExecuteChanged();
        }
    }

    private void OnLikedSongsChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
        => RefreshFavoriteState();

    private void RefreshFavoriteState()
        => IsCurrentSongFavorite = _playlistStore.IsFavorite(PlaybackViewModel.CurrentSong);

    private void ToggleFavorite()
    {
        if (PlaybackViewModel.CurrentSong is not { } song)
        {
            return;
        }

        bool nowFavorite = _playlistStore.ToggleFavorite(song);
        IsCurrentSongFavorite = nowFavorite;
        Notifications.ShowInfo(nowFavorite ? "Added to Liked Songs." : "Removed from Liked Songs.");
    }

    public override void Dispose()
    {
        _navigation.PropertyChanged -= OnNavigationPropertyChanged;
        PlaybackViewModel.PropertyChanged -= OnPlaybackPropertyChanged;
        _playlistStore.LikedSongsPlaylist.Songs.CollectionChanged -= OnLikedSongsChanged;
        SongInfoViewModel.Dispose();
        SeekSliderViewModel.Dispose();
        PlayerViewModel.Dispose();
        ShuffleViewModel.Dispose();
        SearchViewModel.Dispose();
        PlaybackViewModel.Dispose();
        HomeViewModel.Dispose();
        PlaylistViewModel.Dispose();
        RepeatViewModel.Dispose();
        VolumeViewModel.Dispose();
        ToolbarViewModel.Dispose();
        Notifications.Dispose();
        base.Dispose();
    }

    private void OnNavigationPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(NavigationService.CurrentViewModel))
        {
            OnPropertyChanged(nameof(CurrentViewModel));
            OnPropertyChanged(nameof(IsHomeVisible));
            OnPropertyChanged(nameof(IsPlaylistVisible));
            OnPropertyChanged(nameof(IsPlayerVisible));
        }
    }
}
