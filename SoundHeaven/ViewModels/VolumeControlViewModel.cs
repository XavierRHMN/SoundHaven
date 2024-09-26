using System.ComponentModel;
using System.Runtime.CompilerServices;
using SoundHeaven.Commands;
using SoundHeaven.Services;

namespace SoundHeaven.ViewModels
{
    public class VolumeControlViewModel : INotifyPropertyChanged
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
                    _audioService.SetVolume(_volume);
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

        public VolumeControlViewModel(AudioService audioService)
        {
            _audioService = audioService;
            Volume = _audioService.GetCurrentVolume();
            MuteCommand = new RelayCommand(ToggleMute, CanToggleMute);
        }

        private void ToggleMute() => IsMuted = !IsMuted;

        private bool CanToggleMute() => true;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}