using SoundHeaven.Commands;
using System;
using System.Windows.Input;

namespace SoundHeaven.ViewModels
{
    public class RepeatViewModel : ViewModelBase
    {
        private RepeatMode _repeatMode;

        public RepeatMode RepeatMode
        {
            get => _repeatMode;
            set
            {
                if (_repeatMode != value)
                {
                    _repeatMode = value;
                    OnPropertyChanged(nameof(RepeatMode));
                }
            }
        }

        public RelayCommand ToggleRepeatCommand { get; }

        public RepeatViewModel()
        {
            ToggleRepeatCommand = new RelayCommand(ToggleRepeat);
        }

        private void ToggleRepeat()
        {
            RepeatMode = RepeatMode switch
            {
                RepeatMode.Off => RepeatMode.All,
                RepeatMode.All => RepeatMode.One,
                RepeatMode.One => RepeatMode.Off,
                _ => RepeatMode.Off
            };
        }
    }

    public enum RepeatMode
    {
        Off,
        All,
        One
    }
}
