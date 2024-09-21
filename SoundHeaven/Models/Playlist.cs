using SoundHeaven.Services;
using SoundHeaven.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace SoundHeaven.Models
{
    public class Playlist : ViewModelBase
    {
        public string Description { get; set; }
        public ObservableCollection<Song> Songs { get; set; }
        
        private AudioPlayerService _audioPlayerService;
        private MainWindowViewModel _mainWindowViewModel { get; set; }
        private int _currentIndex = 0;
        public int CurrentIndex { get; private set; } = 0;
        
        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    // Raise the PropertyChanged event
                    OnPropertyChanged();
                }
            }
        }

        public Playlist(AudioPlayerService audioPlayerService, MainWindowViewModel mainWindowViewModel)
        {
            Songs = new ObservableCollection<Song>();
            _audioPlayerService = audioPlayerService;
            _mainWindowViewModel = mainWindowViewModel;
        }
        
        public Song? GetNextSong(Song? currentSong)
        {
            if (currentSong == null || Songs == null)
                return null;

            int index = Songs.IndexOf(currentSong);
            if (index >= 0 && index < Songs.Count - 1)
                return Songs[index + 1];

            return Songs.FirstOrDefault(); // Or loop back to the first song if desired
        }

        public Song? GetPreviousSong(Song? currentSong)
        {
            if (currentSong == null || Songs == null)
                return null;

            
            int index = Songs.IndexOf(currentSong);
            if (index > 0)
                return Songs[index - 1];

            return Songs.LastOrDefault(); // Or loop back to the last song if desired
        }
    }
}
