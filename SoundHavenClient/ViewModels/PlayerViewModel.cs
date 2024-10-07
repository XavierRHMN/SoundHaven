using SoundHaven.Models;
using System.Collections;
using System.ComponentModel;
using System.Collections.ObjectModel;

namespace SoundHaven.ViewModels
{
    public class PlayerViewModel : ViewModelBase
    {
        private readonly PlaybackViewModel _playbackViewModel;
        public ObservableCollection<Song>? UpNextSongs
        {
            get => _playbackViewModel.CurrentPlaylist?.Songs;
        }
        
        public string ActivePlaylistName
        {
            get => _playbackViewModel.CurrentPlaylist?.Name ?? "No Active Playlist";
        }

        private Song _playerViewSong;
        public Song PlayerViewSong
        {
            get => _playerViewSong;
            set
            {
                if (SetProperty(ref _playerViewSong, value))
                {
                    // Update the PlaybackViewModel's CurrentSong
                    _playbackViewModel.CurrentSong = value;
                }
            }
        }

        public PlayerViewModel(PlaybackViewModel playbackViewModel)
        {
            _playbackViewModel = playbackViewModel;
            _playbackViewModel.PropertyChanged += PlaybackViewModel_PropertyChanged;
            UpdatePlayerViewSong();
        }

        private void PlaybackViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlaybackViewModel.CurrentSong))
            {
                UpdatePlayerViewSong();
            }
            else if (e.PropertyName == nameof(PlaybackViewModel.CurrentPlaylist))
            {
                OnPropertyChanged(nameof(UpNextSongs));
                OnPropertyChanged(nameof(ActivePlaylistName));
            }
        }

        private void UpdatePlayerViewSong()
        {
            PlayerViewSong = _playbackViewModel.CurrentSong;
        }
    }
}
