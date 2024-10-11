using SoundHaven.Commands;
using SoundHaven.Services;
using System;

namespace SoundHaven.ViewModels
{
    public class VolumeViewModel : ViewModelBase
    {
        private readonly AudioService _audioService;
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

        public VolumeViewModel(AudioService audioService)
        {
            _audioService = audioService;
            MuteCommand = new RelayCommand(ToggleMute);
        }

        private void ToggleMute() => IsMuted = !IsMuted;
    }
}
