﻿using NAudio.Wave;
using SoundHaven.Models;
using System;

namespace SoundHaven.Services
{
    public class AudioService : IDisposable
    {
        private IWavePlayer _waveOutDevice;
        private AudioFileReader _audioFileReader;
        private float _audioVolume = 0.1f;
        public event EventHandler TrackEnded;


        public AudioService()
        {
            _waveOutDevice = new WaveOutEvent();
            _waveOutDevice.PlaybackStopped += OnPlaybackStopped;
        }


        public TimeSpan GetCurrentTime() => _audioFileReader?.CurrentTime ?? TimeSpan.Zero;
        
        public bool IsPlaying() => _waveOutDevice.PlaybackState == PlaybackState.Playing;

        public bool IsStopped() => _waveOutDevice.PlaybackState == PlaybackState.Stopped;

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (_audioFileReader != null && _audioFileReader.Position >= _audioFileReader.Length)
            {
                TrackEnded?.Invoke(this, EventArgs.Empty);
            }
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
            Stop();

            try
            {
                _audioFileReader = new AudioFileReader(filePath);
                _audioFileReader.Volume = _audioVolume;
                _waveOutDevice.Init(_audioFileReader);
                _waveOutDevice.Play();
            
                // Small delay to allow the audio to start properly
            }
            catch (Exception ex)
            {
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
            _waveOutDevice.Stop();
            _audioFileReader?.Dispose();
            _audioFileReader = null;
        }

        public void Restart(Song song)
        {
            try
            {
                Stop();
                Start(song.FilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not restart audio: {ex.Message}");
            }
        }

        public float GetCurrentVolume()
        {
            return _audioVolume;
        }

        public void SetVolume(float volume)
        {
            _audioVolume = volume;
            if (_audioFileReader != null)
            {
                _audioFileReader.Volume = _audioVolume;
            }
        }

        public void Dispose()
        {
            _waveOutDevice.PlaybackStopped -= OnPlaybackStopped;
            _waveOutDevice?.Dispose();
            _audioFileReader?.Dispose();
        }
    }
}