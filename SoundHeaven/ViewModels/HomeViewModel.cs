using System.Collections.ObjectModel;
using SoundHeaven.Models;

namespace SoundHeaven.ViewModels
{
    public class HomeViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainWindowViewModel;

        private Song _currentSong;
        public Song CurrentSong
        {
            get
            {
                return _currentSong;
            }
            set
            {
                if (_currentSong != value)
                {
                    _currentSong = value;
                    OnPropertyChanged();

                    // Set the MainWindowViewModel's CurrentSong to the selected song from the playlist
                    _mainWindowViewModel.CurrentSong = _currentSong;
                }
            }
        }

        // Constructor accepting MainWindowViewModel
        public HomeViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;
        }

    }
}
