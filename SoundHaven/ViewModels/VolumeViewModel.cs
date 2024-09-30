using SoundHaven.Commands;
using SoundHaven.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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
                if (_volume != value)
                {
                    _volume = value;
                    OnPropertyChanged();
                    _audioService.AudioVolume = _volume;
                }
            }
        }

        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                if (_isMuted != value)
                {
                    _isMuted = value;
                    OnPropertyChanged();

                    if (_isMuted)
                    {
                        PreviousVolume = Volume;
                        Volume = 0;
                    }
                    else
                    {
                        Volume = PreviousVolume;
                    }
                }
            }
        }

        public float PreviousVolume
        {
            get => _previousVolume;
            set
            {
                _previousVolume = value;
                OnPropertyChanged();
            }
        }

        public RelayCommand MuteCommand { get; }

        public VolumeViewModel(AudioService audioService)
        {
            _audioService = audioService;
            Volume = _audioService.AudioVolume;
            MuteCommand = new RelayCommand(ToggleMute, CanToggleMute);
        }

        private void ToggleMute() => IsMuted = !IsMuted;

        private bool CanToggleMute() => true;
    }
}
