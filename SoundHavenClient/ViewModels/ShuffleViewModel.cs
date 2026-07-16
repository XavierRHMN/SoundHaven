using System;
using System.ComponentModel;
using SoundHaven.Commands;

namespace SoundHaven.ViewModels;

public sealed class ShuffleViewModel : ViewModelBase
{
    private readonly PlaybackViewModel _playbackViewModel;

    public bool IsShuffleEnabled
    {
        get => _playbackViewModel.IsShuffleEnabled;
        set
        {
            if (_playbackViewModel.IsShuffleEnabled != value)
            {
                _playbackViewModel.IsShuffleEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public RelayCommand ToggleShuffleCommand { get; }

    public ShuffleViewModel(PlaybackViewModel playbackViewModel)
    {
        _playbackViewModel = playbackViewModel
            ?? throw new ArgumentNullException(nameof(playbackViewModel));
        _playbackViewModel.PropertyChanged += OnPlaybackPropertyChanged;
        ToggleShuffleCommand = new RelayCommand(() => IsShuffleEnabled = !IsShuffleEnabled);
    }

    public override void Dispose()
    {
        _playbackViewModel.PropertyChanged -= OnPlaybackPropertyChanged;
        base.Dispose();
    }

    private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(PlaybackViewModel.IsShuffleEnabled))
        {
            OnPropertyChanged(nameof(IsShuffleEnabled));
        }
    }
}
