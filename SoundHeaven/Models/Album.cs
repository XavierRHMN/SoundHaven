using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;

namespace SoundHeaven.Models
{
    public class Album
    {
        public string Title { get; set; }
        public Artist Artist { get; set; }
        public DateTime ReleaseDate { get; set; }
        public Bitmap? CoverArt { get; set; }
        public List<Song> Songs { get; set; }
    }
}
