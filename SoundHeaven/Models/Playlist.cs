using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SoundHeaven.Models
{
    public class Playlist
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public ObservableCollection<Song> Songs { get; set; }
        
        public Playlist()
        {
            Songs = new ObservableCollection<Song>();
        }
    }
}
