using SoundHaven.Data;
using SoundHaven.ViewModels;
using SoundHaven.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace SoundHaven.Models
{
    public class Playlist : ViewModelBase
    {
        public int Id { get; set; }
        
        public ObservableCollection<Song> Songs { get; set; }
        
        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public Playlist()
        {
            Songs = new ObservableCollection<Song>();
        }
        
        public Song? GetPreviousNextSong(Song? currentSong, PlaybackViewModel.Direction direction)
        {
            // Check for nulls and ensure the Songs list is not empty
            if (currentSong == null || Songs == null || Songs.Count == 0)
                return null;

            // Find the index of the current song
            int index = Songs.IndexOf(currentSong);

            // If the current song is not found in the list, return null
            if (index == -1)
                return null;

            // Calculate the new index based on the direction
            int newIndex = (index + (int)direction) % Songs.Count;

            // Handle negative indices to ensure they wrap correctly
            if (newIndex < 0)
                newIndex += Songs.Count;

            // Return the song at the new index
            return Songs[newIndex];
        }
    }
}
