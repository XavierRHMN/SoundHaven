using SoundHeaven.Commands;
using SoundHeaven.Models;
using System;
using System.Windows.Input;

namespace SoundHeaven.ViewModels
{
    public class ShuffleControlViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainWindowViewModel;

        public ShuffleControlViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;

            ToggleShuffleCommand = new RelayCommand(ToggleShuffle);
        }

        private bool _isShuffleEnabled;
        public bool IsShuffleEnabled
        {
            get => _isShuffleEnabled;
            set
            {
                if (_isShuffleEnabled != value)
                {
                    _isShuffleEnabled = value;
                    OnPropertyChanged();

                    // Update the PlaybackControlViewModel
                    _mainWindowViewModel.PlaybackControlViewModel.IsShuffleEnabled = _isShuffleEnabled;
                }
            }
        }

        public RelayCommand ToggleShuffleCommand { get; }

        private void ToggleShuffle()
        {
            _isShuffleEnabled = !_isShuffleEnabled;
        }
    }
}
