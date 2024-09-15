using System.Collections.Generic;

namespace SoundHeaven.Models
{
    public class Playlist
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<Song> Songs { get; set; }
    }
}
