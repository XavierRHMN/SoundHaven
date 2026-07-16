using System;
using SoundHaven.Commands;
using SoundHaven.Services;

namespace SoundHaven.ViewModels;

public sealed class VolumeViewModel : ViewModelBase
{
    private readonly IAudioService _audioService;
    private float _volume;
    private bool _isMuted;
    private float _previousVolume;

    public float Volume
    {
        get => _volume;
        set
        {
            float newVolume = Math.Clamp(value, 0f, 1f);
            if (Math.Abs(_volume - newVolume) > float.Epsilon)
            {
                SetProperty(ref _volume, newVolume);
                _audioService.AudioVolume = _volume;
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                if (_isMuted)
                {
                    PreviousVolume = Volume;
                    Volume = 0;
                    _audioService.AudioVolume = 0f;
                }
                else
                {
                    _audioService.AudioVolume = PreviousVolume;
                    Volume = PreviousVolume;
                }
            }
        }
    }

    public float PreviousVolume
    {
        get => _previousVolume;
        set => SetProperty(ref _previousVolume, value);
    }

    public RelayCommand MuteCommand { get; }

    public VolumeViewModel(IAudioService audioService)
    {
        _audioService = audioService;
        _volume = 0.25f;
        _previousVolume = _volume;
        _audioService.AudioVolume = _volume;
        MuteCommand = new RelayCommand(ToggleMute);
    }

    private void ToggleMute() => IsMuted = !IsMuted;
}
