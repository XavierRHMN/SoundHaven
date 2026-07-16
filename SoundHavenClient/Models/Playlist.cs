using System.Collections.ObjectModel;
using SoundHaven.ViewModels;

namespace SoundHaven.Models
{
    public class Playlist : ViewModelBase
    {
        public ObservableCollection<Song> Songs { get; set; } = new();

        public int Id { get; set; }

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value ?? string.Empty);
        }

        public Song? GetPreviousNextSong(Song? currentSong, PlaybackViewModel.Direction direction)
        {
            if (currentSong == null || Songs.Count == 0)
            {
                return null;
            }

            int index = Songs.IndexOf(currentSong);
            if (index < 0)
            {
                return null;
            }

            int newIndex = (index + (int)direction) % Songs.Count;
            if (newIndex < 0)
            {
                newIndex += Songs.Count;
            }

            return Songs[newIndex];
        }
    }
}
