using System.Collections.Generic;

namespace SoundHeaven.Models
{
    public class Artist
    {
        public string Name { get; set; }
        public string Bio { get; set; }
        public List<Album> Albums { get; set; }
    }
}
