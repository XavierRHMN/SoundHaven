using NAudio.Wave;
using System;

namespace SoundHeaven.Services
{
    public class AudioPlayerService : IDisposable
    {
        private IWavePlayer _waveOutDevice;
        private AudioFileReader _audioFileReader;

        public AudioPlayerService()
        {
            _waveOutDevice = new WaveOutEvent(); // You can replace this with a different output device if needed
        }

        public void Play(string filePath)
        {
            Stop(); // Ensure any currently playing audio is stopped before starting a new one

            try
            {
                _audioFileReader = new AudioFileReader(filePath);
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

        public void SetVolume(float volume)
        {
            if (_audioFileReader != null)
            {
                _audioFileReader.Volume = volume; // Volume should be between 0.0 and 1.0
            }
        }

        public void Dispose()
        {
            _waveOutDevice?.Dispose();
            _audioFileReader?.Dispose();
        }
    }
}
