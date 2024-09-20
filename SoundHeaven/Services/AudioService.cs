using NAudio.Wave;
using System;

namespace SoundHeaven.Services
{
    public class AudioPlayerService : IDisposable
    {
        private IWavePlayer _waveOutDevice;
        private AudioFileReader _audioFileReader;
        private double _audioVolume = 0.5;

        public AudioPlayerService()
        {
            _waveOutDevice = new WaveOutEvent(); // You can replace this with a different output device if needed
        }

        public TimeSpan GetCurrentTime() => _audioFileReader?.CurrentTime ?? TimeSpan.Zero;

        public bool IsPlaying()
        {
            return _waveOutDevice.PlaybackState == PlaybackState.Playing;
        }
        
        public bool IsStopped()
        {
            return _waveOutDevice.PlaybackState == PlaybackState.Stopped;
        }
        
        
        public void Seek(TimeSpan position)
        {
            if (_audioFileReader != null)
            {
                _audioFileReader.CurrentTime = position;
            }
        }

        public void Start(string filePath)
        {
            Stop(); // Ensure any currently playing audio is stopped before starting a new one

            try
            {
                _audioFileReader = new AudioFileReader(filePath);
                _audioFileReader.Volume = (float)_audioVolume; // Set the current volume when loading the new song
                _waveOutDevice.Init(_audioFileReader);
                _waveOutDevice.Play();
            }
            catch (Exception ex)
            {
                // Handle exceptions (e.g., file not found, invalid format)
                Console.WriteLine($"Error playing audio: {ex.Message}");
            }
        }

        public void Pause()
        {
            if (_waveOutDevice.PlaybackState == PlaybackState.Playing)
            {
                _waveOutDevice.Pause();
            }
        }

        public void Resume()
        {
            if (_waveOutDevice.PlaybackState == PlaybackState.Paused)
            {
                _waveOutDevice.Play();
            }
        }

        public void Stop()
        {
            if (_waveOutDevice.PlaybackState == PlaybackState.Playing || _waveOutDevice.PlaybackState == PlaybackState.Paused)
            {
                _waveOutDevice.Stop();
                _audioFileReader?.Dispose();
                _audioFileReader = null;
            }
        }

        public double GetCurrentVolume()
        {
            _audioFileReader.Volume = (float)_audioVolume;
            return _audioFileReader.Volume;
        }

        public void SetVolume(float volume)
        {
            _audioVolume = volume;
            _audioFileReader.Volume = (float)_audioVolume;

        }

        public void Dispose()
        {
            _waveOutDevice?.Dispose();
            _audioFileReader?.Dispose();
        }
    }
}
