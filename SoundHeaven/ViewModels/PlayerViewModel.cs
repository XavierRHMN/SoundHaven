using System.Collections.ObjectModel;
using System.ComponentModel;
using SoundHeaven.Models;

namespace SoundHeaven.ViewModels
{
    public class PlayerViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        
        public ObservableCollection<Song> UpNextSongs => _mainWindowViewModel.CurrentPlaylist?.Songs;
        
        private Song _playerViewSong;
        public Song PlayerViewSong
        {
            get => _playerViewSong;
            set // Changed from private to public
            {
                if (_playerViewSong != value)
                {
                    _playerViewSong = value;
                    OnPropertyChanged();
                    
                    // Update the MainWindowViewModel's CurrentSong
                    _mainWindowViewModel.CurrentSong = value;
                }
            }
        }
        
        public PlayerViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;
            _mainWindowViewModel.PropertyChanged += MainWindowViewModel_PropertyChanged;
            UpdatePlayerViewSong();
        }

        private void MainWindowViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentSong))
            {
                UpdatePlayerViewSong();
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.CurrentPlaylist))
            {
                OnPropertyChanged(nameof(UpNextSongs));
            }
        }

        private void UpdatePlayerViewSong()
        {
            PlayerViewSong = _mainWindowViewModel.CurrentSong;
        }

        public void Dispose()
        {
            _mainWindowViewModel.PropertyChanged -= MainWindowViewModel_PropertyChanged;
        }
    }
}
