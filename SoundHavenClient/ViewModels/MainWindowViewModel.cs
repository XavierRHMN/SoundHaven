using System;
using System.ComponentModel;
using SoundHaven.Services;

namespace SoundHaven.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;

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
        ThemesViewModel themesViewModel,
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
        ThemesViewModel = themesViewModel;
        RepeatViewModel = repeatViewModel;
        SearchViewModel = searchViewModel;
        Notifications = notifications;

        _navigation.PropertyChanged += OnNavigationPropertyChanged;
    }

    public ViewModelBase CurrentViewModel => _navigation.CurrentViewModel;

    public PlaylistViewModel PlaylistViewModel { get; }

    public HomeViewModel HomeViewModel { get; }

    public ToolbarViewModel ToolbarViewModel { get; }

    public PlaybackViewModel PlaybackViewModel { get; }

    public ShuffleViewModel ShuffleViewModel { get; }

    public PlayerViewModel PlayerViewModel { get; }

    public SeekSliderViewModel SeekSliderViewModel { get; }

    public VolumeViewModel VolumeViewModel { get; }

    public SongInfoViewModel SongInfoViewModel { get; }

    public ThemesViewModel ThemesViewModel { get; }

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
        ThemesViewModel.Dispose();
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
        }
    }
}
