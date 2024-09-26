using SoundHeaven.Services;
using SoundHeaven.ViewModels;
using System.Collections.ObjectModel;
using System.Linq;

namespace SoundHeaven.Models
{
    public class PlaylistStore : ViewModelBase
    {
        private ObservableCollection<Playlist>? _playlists;
        public ObservableCollection<Playlist> Playlists
        {
            get => _playlists;
            set => _playlists = value;
        }

        private MainWindowViewModel _mainWindowViewModel;
        private ToolbarViewModel _toolbarViewModel;
        private AudioService _audioService;

        public PlaylistStore(MainWindowViewModel mainWindowViewModel)
        {
            Playlists = new ObservableCollection<Playlist>();
            _mainWindowViewModel = mainWindowViewModel;
            _audioService = _mainWindowViewModel.AudioService;
            _toolbarViewModel = _mainWindowViewModel.ToolbarViewModel;
        }

        public void AddPlaylist(Playlist playlist)
        {
            Playlists.Add(playlist);
        }

        public void RemovePlaylist(Playlist playlist)
        {
            if (Playlists.Contains(playlist))
            {
                Playlists.Remove(playlist);
            }
        }
    }
}
