using Avalonia.Controls;
using SoundHeaven.Services;
using System;

namespace SoundHeaven.Models
{
    public class Song
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public TimeSpan Duration { get; set; }
        public string? FilePath { get; set; }
        public Image? Artwork { get; set; }
        public string? ArtworkFilePath { get; set; }
        public string? Genre { get; set; }
        public int Year { get; set; }
        public double Length => Duration.TotalSeconds;

        private AudioPlayerService _audioPlayerService;

        public Song(AudioPlayerService audioPlayerService) => _audioPlayerService = audioPlayerService;

        public void Play()
        {
            if (!string.IsNullOrEmpty(FilePath))
            {
                _audioPlayerService.Start(FilePath);
            }
        }

        public void Pause()
        {
            _audioPlayerService.Pause();
        }

        public void Resume()
        {
            _audioPlayerService.Resume();
        }

        public void Stop()
        {
            _audioPlayerService.Stop();
        }
    }
}
