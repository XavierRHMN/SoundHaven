using System;
using System.ComponentModel;
using SoundHaven.Commands;
using SoundHaven.Services;

namespace SoundHaven.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;
    private bool _isQueueVisible;

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
        NotificationService notifications)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
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
        _navigation.PropertyChanged += OnNavigationPropertyChanged;
    }

    /// <summary>Docked play-queue panel on the right of the content area.</summary>
    public bool IsQueueVisible
    {
        get => _isQueueVisible;
        set => SetProperty(ref _isQueueVisible, value);
    }

    public RelayCommand ToggleQueueCommand { get; }

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

    public override void Dispose()
    {
        _navigation.PropertyChanged -= OnNavigationPropertyChanged;
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
