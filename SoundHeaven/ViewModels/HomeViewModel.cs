using System.Collections.ObjectModel;
using SoundHeaven.Models;

namespace SoundHeaven.ViewModels
{
    public class HomeViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel _mainWindowViewModel;

        // Expose the SongCollection from MainWindowViewModel
        public ObservableCollection<Song> HomeViewSongs => _mainWindowViewModel.SongCollection;

        private Song _homeViewCurrentSong;
        public Song HomeViewCurrentSong
        {
            get => _homeViewCurrentSong;
            set
            {
                if (_homeViewCurrentSong != value)
                {
                    _homeViewCurrentSong = value;
                    OnPropertyChanged();

                    // Set the MainWindowViewModel's CurrentSong to the selected song
                    _mainWindowViewModel.CurrentSong = _homeViewCurrentSong;
                }
            }
        }

        // Constructor that accepts MainWindowViewModel
        public HomeViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;

            // Subscribe to CurrentSong property changes in MainWindowViewModel
            _mainWindowViewModel.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.CurrentSong))
                {
                    // Update HomeViewCurrentSong when MainWindowViewModel's CurrentSong changes
                    HomeViewCurrentSong = _mainWindowViewModel.CurrentSong;
                }
            };
        }
    }
}
