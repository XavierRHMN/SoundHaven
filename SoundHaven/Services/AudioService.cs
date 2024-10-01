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
        private const int BufferSize = 4 * 1024 * 1024; // 4MB buffer

        private readonly YoutubeClient _youtubeClient;
        private IYouTubeDownloadService _youTubeDownloadService;
        private IWavePlayer _waveOutDevice;
        private AudioFileReader _audioFileReader;
        private BufferedWaveProvider _bufferedWaveProvider;
        private VolumeSampleProvider _volumeProvider;
        private CancellationTokenSource _bufferingCancellationTokenSource;
        private Timer _positionLogTimer;
        private Timer _bufferStatusTimer;
        private DateTime _streamStartTime;
        private TimeSpan _currentPosition;
        private TimeSpan _totalDuration;
        private TimeSpan _startPosition;
        private bool _isBuffering;
        private bool _isDisposed;
        private bool _isYouTubeStream;
        private string _currentSource;
        private bool _isSeeking = false;
        private TimeSpan _accumulatedPauseDuration = TimeSpan.Zero;
        private DateTime _pauseStartTime;
        private bool _isPaused = false;
        public bool IsPaused
        {
            get => _isPaused;
            private set
            {
                if (_isPaused != value)
                {
                    _isPaused = value;
                    OnPropertyChanged();
                }
            }
        }

        public event EventHandler TrackEnded;
        public event EventHandler PlaybackStateChanged;

        public TimeSpan CurrentPosition => _currentPosition;

        private float _audioVolume = 0.1f;
        public float AudioVolume
        {
            get => _audioVolume;
            set
            {
                if (_audioVolume != value)
                {
                    _audioVolume = value;
                    if (_volumeProvider != null)
                    {
                        _volumeProvider.Volume = _audioVolume;
                    }
                    OnPropertyChanged();
                }
            }
        }

        public TimeSpan TotalDuration
        {
            get => _totalDuration;
            private set
            {
                if (_totalDuration != value)
                {
                    _totalDuration = value;
                    OnPropertyChanged();
                }
            }
        }
        
        private bool _isSeekBuffering;
        public bool IsSeekBuffering
        {
            get => _isSeekBuffering;
            private set
            {
                if (_isSeekBuffering != value)
                {
                    _isSeekBuffering = value;
                    OnPropertyChanged();
                }
            }
        }

        public AudioService()
        {
            _waveOutDevice = new WaveOutEvent();
            _waveOutDevice.PlaybackStopped += OnPlaybackStopped;
            _youtubeClient = new YoutubeClient();
            _youTubeDownloadService = new YouTubeDownloadService();
        }

        public TimeSpan GetCurrentTime() => _audioFileReader?.CurrentTime ?? TimeSpan.Zero;

        public bool IsPlaying()
        {
            var isPlaying = _waveOutDevice?.PlaybackState == PlaybackState.Playing;
            Console.WriteLine($"IsPlaying called, result: {isPlaying}");
            return isPlaying;
        }

        public bool IsStopped() => _waveOutDevice?.PlaybackState == PlaybackState.Stopped;

        public async Task StartAsync(string source, bool isYouTubeVideo = false, TimeSpan startingPosition = default)
        {
            // Do not reset position variables
            Stop(resetPosition: false);

            _currentSource = source;
            _isYouTubeStream = isYouTubeVideo;
            _startPosition = startingPosition;
            _currentPosition = startingPosition;
            _streamStartTime = DateTime.Now;

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
        }
        
        public void Seek(TimeSpan position)
        {
            _accumulatedPauseDuration = TimeSpan.Zero;
            _isPaused = false;
            
            if (_audioFileReader != null)
            {
                _audioFileReader.CurrentTime = position;
                OnPropertyChanged(nameof(CurrentPosition));
            }
            else if (_isYouTubeStream)
            {
                // Stop current playback and buffering without resetting position
                Stop(resetPosition: false);

                // Update start position and current position
                _startPosition = position;
                _currentPosition = position;

                // Start playback from the new position
                _ = StartAsync(_currentSource, true, position);
            }
        }

        public void Pause()
        {
            if (_waveOutDevice?.PlaybackState == PlaybackState.Playing)
            {
                _waveOutDevice.Pause();
                _pauseStartTime = DateTime.Now;
                IsPaused = true;
                PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Resume()
        {
            if (_waveOutDevice?.PlaybackState == PlaybackState.Paused)
            {
                _waveOutDevice.Play();
                _accumulatedPauseDuration += DateTime.Now - _pauseStartTime;
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

            _accumulatedPauseDuration = TimeSpan.Zero;
            _isPaused = false;
            
            _waveOutDevice?.Stop();
            _audioFileReader?.Dispose();
            _audioFileReader = null;
            _bufferedWaveProvider = null;
            _volumeProvider = null;
            _isYouTubeStream = false;

            if (resetPosition)
            {
                _currentPosition = TimeSpan.Zero;
                _startPosition = TimeSpan.Zero;
                _totalDuration = TimeSpan.Zero;
                OnPropertyChanged(nameof(CurrentPosition));
            }

            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
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

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if ((_audioFileReader != null && _audioFileReader.CurrentTime.TotalSeconds + 5 >= _audioFileReader.TotalTime.TotalSeconds) || (_bufferedWaveProvider != null && _bufferedWaveProvider.BufferedBytes == 0 && !_isBuffering))
            {
                TrackEnded?.Invoke(this, EventArgs.Empty);
            }
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
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

                var streamUrl = streamInfo.Url;

                var waveFormat = new WaveFormat(44100, 16, 2);
                _bufferedWaveProvider = new BufferedWaveProvider(waveFormat)
                {
                    BufferLength = 8 * 1024 * 1024 // 8MB buffer
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

                    while (_bufferedWaveProvider.BufferedBytes > _bufferedWaveProvider.BufferLength * 0.8)
                    {
                        await Task.Delay(50, cancellationToken);
                    }
                }

                ffmpegProcess.WaitForExit();
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Buffering operation was canceled.");
                IsSeekBuffering = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in BufferYouTubeStreamAsync: {ex.Message}");
                IsSeekBuffering = true;
            }
            finally
            {
                IsSeekBuffering = true;
                _isBuffering = false;
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
                Volume = _audioVolume
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
            if (_bufferedWaveProvider != null)
            {
                Console.WriteLine($"Buffer status: {_bufferedWaveProvider.BufferedBytes} / {_bufferedWaveProvider.BufferLength} bytes");

                var bufferThreshold = _bufferedWaveProvider.WaveFormat.AverageBytesPerSecond * 2; // 2 seconds of audio
                if (_bufferedWaveProvider.BufferedBytes < bufferThreshold && !_isBuffering)
                {
                    Console.WriteLine("Buffer underrun detected. Pausing playback.");
                    _isBuffering = true;

                    // Pause playback
                    _waveOutDevice.Pause();

                    // Check if we're near the end of the track
                    TimeSpan remainingTime = _totalDuration - _currentPosition;
                    if (remainingTime <= TimeSpan.FromSeconds(2))
                    {
                        // We're near the end, so don't seek back
                        Console.WriteLine("Near end of track. Not seeking back.");
                        _waveOutDevice.Play();
                        _isBuffering = false;
                    }
                    else
                    {
                        // Adjust current position back by 0.5 seconds
                        TimeSpan newPosition = _currentPosition - TimeSpan.FromSeconds(0.5);
                        if (newPosition < TimeSpan.Zero)
                        {
                            newPosition = TimeSpan.Zero;
                        }

                        // Stop current playback and buffering without resetting position
                        Stop(resetPosition: false);

                        // Update start position and current position
                        _startPosition = newPosition;
                        _currentPosition = newPosition;

                        // Start playback from the new position
                        _ = StartAsync(_currentSource, true, newPosition);
                    }
                }
                else if (_bufferedWaveProvider.BufferedBytes > _bufferedWaveProvider.BufferLength * 0.5 && _isBuffering)
                {
                    Console.WriteLine("Buffer refilled. Resuming playback.");
                    _waveOutDevice.Play();
                    _isBuffering = false;
                    StartPositionLogging();
                }
            }
        }

        private void StartPositionLogging()
        {
            _positionLogTimer?.Dispose();
            _streamStartTime = DateTime.Now;
            _accumulatedPauseDuration = TimeSpan.Zero; // Reset when starting position logging
            _positionLogTimer = new Timer(_ => UpdateCurrentPosition(), null, 0, 100);
        }

        private void UpdateCurrentPosition()
        {
            if (_isYouTubeStream)
            {
                var elapsed = DateTime.Now - _streamStartTime - _accumulatedPauseDuration;
                _currentPosition = _startPosition + elapsed;
                if (_currentPosition > _totalDuration)
                {
                    _currentPosition = _totalDuration;
                }
                OnPropertyChanged(nameof(CurrentPosition));
            }
            else
            {
                _currentPosition = _audioFileReader?.CurrentTime ?? TimeSpan.Zero;
                OnPropertyChanged(nameof(CurrentPosition));
            }
        }
    }
}