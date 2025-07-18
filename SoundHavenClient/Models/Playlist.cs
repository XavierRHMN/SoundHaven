﻿using SoundHaven.ViewModels;
using System.Collections.ObjectModel;

namespace SoundHaven.Models
{
    public class Playlist : ViewModelBase
    {
        
        public ObservableCollection<Song> Songs { get; set; }

        public int Id { get; set; }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
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
