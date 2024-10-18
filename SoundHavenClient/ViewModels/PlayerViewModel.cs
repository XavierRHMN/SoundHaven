using SoundHaven.Models;
using SoundHaven.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

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

        private Song? _playerViewSong;
        public Song? PlayerViewSong
        {
            get => _playbackViewModel.CurrentSong;
            set
            {
                if (SetProperty(ref _playerViewSong, value))
                {
                    _playbackViewModel.CurrentSong = value;
                }
            }
        }

        public PlayerViewModel(PlaybackViewModel playbackViewModel)
        {
            _playbackViewModel = playbackViewModel;
            _playbackViewModel.PropertyChanged += PlaybackViewModel_PropertyChanged;
        }

        private void PlaybackViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(PlaybackViewModel.CurrentSong):
                    OnPropertyChanged(nameof(PlayerViewSong));
                    break;
                case nameof(PlaybackViewModel.CurrentPlaylist):
                    OnPropertyChanged(nameof(UpNextSongs));
                    OnPropertyChanged(nameof(ActivePlaylistName));
                    break;
            }
        }
    }
}