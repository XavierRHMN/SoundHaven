﻿using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SoundHaven.ViewModels;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SoundHaven.Services
{
    public class AudioService : ViewModelBase, IDisposable
    {
        private const int BufferSize = 80 * 1024 * 1024; // 80MB buffer

        private readonly YoutubeClient _youtubeClient;
        private TimeSpan _totalPauseTime = TimeSpan.Zero;
        private AudioFileReader _audioFileReader;

        private float _audioVolume = 1.0f;
        private BufferedWaveProvider _bufferedWaveProvider;
        private CancellationTokenSource _bufferingCancellationTokenSource;
        private Timer _bufferStatusTimer;
        private TimeSpan _currentYoutubeTime;
        private string _currentSource;
        private bool _isBuffering;
        private bool _isDisposed;
        private bool _isPaused;
        private bool _isTrackEnded;

        private bool _isSeekBuffering;
        private bool _isYouTubeStream;
        private DateTime _currentPauseStartTime;
        private Timer _positionLogTimer;
        private TimeSpan _startTime;
        private DateTime _playbackStartTime;
        private TimeSpan _totalDuration;
        private VolumeSampleProvider _volumeProvider;
        private IWavePlayer _waveOutDevice;
        private IYouTubeDownloadService _youTubeDownloadService;

        public AudioService()
        {
            _waveOutDevice = new WaveOutEvent();
            _waveOutDevice.PlaybackStopped += OnPlaybackStopped;
            _youtubeClient = new YoutubeClient();
            _youTubeDownloadService = new YouTubeDownloadService();
        }

        public bool IsPaused
        {
            get => _isPaused;
            private set => SetProperty(ref _isPaused, value);
        }

        public TimeSpan CurrentLocalPosition
        {
            get => _audioFileReader?.CurrentTime ?? TimeSpan.Zero;
        }
        
        public TimeSpan CurrentYoutubePosition
        {
            get => _currentYoutubeTime;
        }

        public float AudioVolume
        {
            get => _audioVolume;
            set
            {
                if (SetProperty(ref _audioVolume, value))
                {
                    _audioVolume = Math.Clamp(value, 0f, 1f);
                    if (_volumeProvider != null)
                    {
                        _volumeProvider.Volume = (float)Math.Pow(_audioVolume, 2);
                    }
                }
            }
        }
        
        public TimeSpan TotalDuration
        {
            get => _totalDuration;
            private set => SetProperty(ref _totalDuration, value);
        }
        
        public bool IsSeekBuffering
        {
            get => _isSeekBuffering;
            private set => SetProperty(ref _isSeekBuffering, value);
        }

        public bool IsPlaying
        {
            get => _waveOutDevice?.PlaybackState == PlaybackState.Playing;
        }
        
        public bool IsStopped
        {
            get => _waveOutDevice?.PlaybackState == PlaybackState.Stopped;
        } 
        
        public event EventHandler TrackEnded;
        public event EventHandler PlaybackStateChanged;

        protected virtual void OnPlaybackStateChanged()
        {
            if (PlaybackStateChanged != null)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                });
            }
        }

        public async Task StartAsync(string source, bool isYouTubeVideo = false, TimeSpan startingPosition = default)
        {
            // Do not reset position variables
            Stop(false);

            _isTrackEnded = false; // Reset the flag

            _currentSource = source;
            _isYouTubeStream = isYouTubeVideo;
            _startTime = startingPosition;
            _currentYoutubeTime = startingPosition;
            _playbackStartTime = DateTime.Now;

            try
            {
                if (isYouTubeVideo)
                {
                    string cleanVideoId = _youTubeDownloadService.CleanVideoId(source);
                    await StartYouTubeStreamAsync(cleanVideoId, startingPosition);
                }
                else
                {
                    StartLocalFile(source);
                    if (startingPosition != default)
                    {
                        Seek(startingPosition);
                    }
                }

                if (_waveOutDevice != null && (_audioFileReader != null || _bufferedWaveProvider != null))
                {
                    _waveOutDevice.Play();
                    PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                    if (isYouTubeVideo)
                    {
                        StartBufferStatusTimer();
                        StartPositionLogging();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AudioService.StartAsync: {ex.Message}");
                throw;
            }
            OnPlaybackStateChanged();
        }

        public void Seek(TimeSpan position)
        {
            _totalPauseTime = TimeSpan.Zero;
            _isPaused = false;

            if (_audioFileReader != null)
            {
                _audioFileReader.CurrentTime = position;
                OnPropertyChanged(nameof(CurrentYoutubePosition));
            }
            else if (_isYouTubeStream)
            {
                // Stop current playback and buffering without resetting position
                Stop(false);

                // Update start position and current position
                _startTime = position;
                _currentYoutubeTime = position;

                // Start playback from the new position
                _ = StartAsync(_currentSource, true, position);
            }
        }

        public void Pause()
        {
            if (_waveOutDevice?.PlaybackState == PlaybackState.Playing)
            {
                _waveOutDevice.Pause();
                _currentPauseStartTime = DateTime.Now;
                IsPaused = true;
                PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Resume()
        {
            if (_waveOutDevice?.PlaybackState == PlaybackState.Paused)
            {
                _waveOutDevice.Play();
                _totalPauseTime += DateTime.Now - _currentPauseStartTime;
                IsPaused = false;
                PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Stop(bool resetPosition = true)
        {
            _isBuffering = false;
            _bufferStatusTimer?.Dispose();
            _positionLogTimer?.Dispose();
            _bufferingCancellationTokenSource?.Cancel();

            _totalPauseTime = TimeSpan.Zero;
            _isPaused = false;

            _waveOutDevice?.Stop();
            _audioFileReader?.Dispose();
            _audioFileReader = null;
            _bufferedWaveProvider = null;
            _volumeProvider = null;
            _isYouTubeStream = false;

            _isTrackEnded = true; // Set the flag

            if (resetPosition)
            {
                _currentYoutubeTime = TimeSpan.Zero;
                _startTime = TimeSpan.Zero;
                _totalDuration = TimeSpan.Zero;
                OnPropertyChanged(nameof(CurrentYoutubePosition));
            }

            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (IsPaused)
            {
                // If the player is paused, we don't need to handle this event
                return;
            }

            if (_audioFileReader != null && _audioFileReader.CurrentTime.TotalSeconds + 5 >= _audioFileReader.TotalTime.TotalSeconds ||
                _bufferedWaveProvider != null && _bufferedWaveProvider.BufferedBytes == 0 && !_isBuffering)
            {
                _isTrackEnded = true; // Set the flag
                TrackEnded?.Invoke(this, EventArgs.Empty);
            }
            OnPlaybackStateChanged();
        }



        private async Task StartYouTubeStreamAsync(string videoId, TimeSpan startingPosition)
        {
            try
            {
                var streamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(videoId);
                var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                if (streamInfo == null)
                {
                    throw new InvalidOperationException("No audio stream found for this video.");
                }

                string streamUrl = streamInfo.Url;

                var waveFormat = new WaveFormat(44100, 16, 2);
                // Change this line in your StartYouTubeStreamAsync method
                _bufferedWaveProvider = new BufferedWaveProvider(waveFormat)
                {
                    BufferLength = 80 * 1024 * 1024 // 80MB buffer
                };

                InitializeAudio(_bufferedWaveProvider);

                _isBuffering = true;
                _bufferingCancellationTokenSource = new CancellationTokenSource();

                var videoDetails = await _youtubeClient.Videos.GetAsync(videoId);
                TotalDuration = videoDetails.Duration ?? TimeSpan.Zero;

                _ = Task.Run(() => BufferYouTubeStreamAsync(streamUrl, startingPosition, _bufferingCancellationTokenSource.Token));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in StartYouTubeStreamAsync: {ex.Message}");
                throw;
            }
        }

        private async Task BufferYouTubeStreamAsync(string streamUrl, TimeSpan startingPosition, CancellationToken cancellationToken)
        {
            try
            {
                using var ffmpegProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = $"-ss {startingPosition.TotalSeconds} -i \"{streamUrl}\" -f s16le -ar 44100 -ac 2 pipe:1",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                ffmpegProcess.Start();

                IsSeekBuffering = false;

                byte[] buffer = new byte[BufferSize];
                int bytesRead;

                while ((bytesRead = await ffmpegProcess.StandardOutput.BaseStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    _bufferedWaveProvider.AddSamples(buffer, 0, bytesRead);

                    while (_bufferedWaveProvider.BufferedBytes > _bufferedWaveProvider.BufferLength * 0.9)
                    {
                        await Task.Delay(50, cancellationToken);
                    }
                }

                ffmpegProcess.WaitForExit();
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Buffering operation was canceled.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in BufferYouTubeStreamAsync: {ex.Message}");
            }
            finally
            {
                _isBuffering = false;
                IsSeekBuffering = true;
            }
        }

        private void StartLocalFile(string filePath)
        {
            _audioFileReader = new AudioFileReader(filePath);
            var silenceProvider = new SilenceProvider(_audioFileReader.WaveFormat).ToSampleProvider();
            var silenceDuration = TimeSpan.FromSeconds(0.5);

            var silence = new OffsetSampleProvider(silenceProvider) { Take = silenceDuration };

            var composite = new ConcatenatingSampleProvider(new[] { silence, _audioFileReader.ToSampleProvider() });
            InitializeAudio(composite.ToWaveProvider());
            TotalDuration = _audioFileReader.TotalTime + silenceDuration;
        }

        private void InitializeAudio(IWaveProvider waveProvider)
        {
            _volumeProvider = new VolumeSampleProvider(waveProvider.ToSampleProvider())
            {
                Volume = (float)Math.Pow(_audioVolume, 2)
            };

            _waveOutDevice.Init(_volumeProvider);
        }

        private void StartBufferStatusTimer()
        {
            _bufferStatusTimer?.Dispose();
            _bufferStatusTimer = new Timer(_ => CheckBufferStatus(), null, 0, 500);
        }

        private void CheckBufferStatus()
        {
            if (_isTrackEnded)
            {
                // Song has ended, do not attempt to refill
                return;
            }

            if (_bufferedWaveProvider != null)
            {
                Console.WriteLine($"Buffer status: {_bufferedWaveProvider.BufferedBytes} / {_bufferedWaveProvider.BufferLength} bytes");

                int bufferThreshold = _bufferedWaveProvider.WaveFormat.AverageBytesPerSecond * 1; // 1 second of audio
                if (_bufferedWaveProvider.BufferedBytes < bufferThreshold && !_isBuffering)
                {
                    Console.WriteLine("Buffer getting low. Starting to refill.");
                    _isBuffering = true;

                    // Start refilling the buffer
                    _ = RefillBufferAsync();
                }
            }
        }

        private async Task RefillBufferAsync()
        {
            if (_isTrackEnded)
            {
                // Song has ended, do not attempt to refill
                return;
            }

            try
            {
                var streamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(_currentSource);
                var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                if (streamInfo == null)
                {
                    throw new InvalidOperationException("No audio stream found for this video.");
                }

                string streamUrl = streamInfo.Url;

                using var ffmpegProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = $"-ss {_currentYoutubeTime.TotalSeconds} -i \"{streamUrl}\" -f s16le -ar 44100 -ac 2 pipe:1",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                ffmpegProcess.Start();

                byte[] buffer = new byte[BufferSize];
                int bytesRead;

                while ((bytesRead = await ffmpegProcess.StandardOutput.BaseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    _bufferedWaveProvider.AddSamples(buffer, 0, bytesRead);

                    if (_bufferedWaveProvider.BufferedBytes > _bufferedWaveProvider.BufferLength * 0.9)
                    {
                        break;
                    }
                }

                ffmpegProcess.WaitForExit();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RefillBufferAsync: {ex.Message}");
            }
            finally
            {
                _isBuffering = false;
            }
        }

        private void StartPositionLogging()
        {
            _positionLogTimer?.Dispose();
            _playbackStartTime = DateTime.Now;
            _totalPauseTime = TimeSpan.Zero; // Reset when starting position logging
            _positionLogTimer = new Timer(_ => UpdateCurrentPosition(), null, 0, 100);
        }

        private void UpdateCurrentPosition()
        {
            if (_isYouTubeStream)
            {
                var currentTotalPauseTime = _totalPauseTime;
                if (IsPaused)
                {
                    currentTotalPauseTime += DateTime.Now - _currentPauseStartTime;
                }
                
                var totalElapsedPlaybackTime = DateTime.Now - _playbackStartTime - currentTotalPauseTime;
                _currentYoutubeTime = _startTime + totalElapsedPlaybackTime;
                
                if (_currentYoutubeTime >= _totalDuration)
                {
                    _currentYoutubeTime = _totalDuration;

                    if (!_isTrackEnded)
                    {
                        _isTrackEnded = true;

                        // Stop playback and raise the TrackEnded event
                        Stop();
                        TrackEnded?.Invoke(this, EventArgs.Empty);
                    }
                }
                OnPropertyChanged(nameof(CurrentYoutubePosition));
            }
            else
            {
                _currentYoutubeTime = _audioFileReader?.CurrentTime ?? TimeSpan.Zero;

                if (_currentYoutubeTime >= _totalDuration)
                {
                    _currentYoutubeTime = _totalDuration;

                    if (!_isTrackEnded)
                    {
                        _isTrackEnded = true;

                        // Stop playback and raise the TrackEnded event
                        Stop();
                        TrackEnded?.Invoke(this, EventArgs.Empty);
                    }
                }
                OnPropertyChanged(nameof(CurrentYoutubePosition));
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            Stop();
            _waveOutDevice?.Dispose();
            _bufferingCancellationTokenSource?.Dispose();
            _bufferStatusTimer?.Dispose();
            _positionLogTimer?.Dispose();
        }
    }
}
