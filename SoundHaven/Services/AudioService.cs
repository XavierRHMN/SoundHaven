using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SoundHaven.ViewModels;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using System.Diagnostics;

namespace SoundHaven.Services
{
    public class AudioService : ViewModelBase, IDisposable
    {
        private const int BufferSize = 4 * 1024 * 1024; // 4MB buffer

        private readonly YoutubeClient _youtubeClient;
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
        private bool _isBuffering;
        private bool _isDisposed;
        private bool _isYouTubeStream;

        public event EventHandler TrackEnded;

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

        public AudioService()
        {
            _waveOutDevice = new WaveOutEvent();
            _waveOutDevice.PlaybackStopped += OnPlaybackStopped;
            _youtubeClient = new YoutubeClient();
        }

        public TimeSpan GetCurrentTime() => _audioFileReader?.CurrentTime ?? TimeSpan.Zero;

        public bool IsPlaying()
        {
            var isPlaying = _waveOutDevice?.PlaybackState == PlaybackState.Playing;
            Console.WriteLine($"IsPlaying called, result: {isPlaying}");
            return isPlaying;
        }

        public bool IsStopped() => _waveOutDevice?.PlaybackState == PlaybackState.Stopped;

        public float GetCurrentVolume() => _audioVolume;
        
        public async Task StartAsync(string source, bool isYouTubeVideo = false)
        {
            Stop();

            try
            {
                _isYouTubeStream = isYouTubeVideo;
                if (isYouTubeVideo)
                {
                    await StartYouTubeStreamAsync(source);
                }
                else
                {
                    StartLocalFile(source);
                }

                if (_waveOutDevice != null && (_audioFileReader != null || _bufferedWaveProvider != null))
                {
                    _waveOutDevice.Play();
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
            if (_audioFileReader != null)
            {
                _audioFileReader.CurrentTime = position;
            }
        }

        public void Pause()
        {
            if (_waveOutDevice?.PlaybackState == PlaybackState.Playing)
            {
                _waveOutDevice.Pause();
            }
        }

        public void Resume()
        {
            if (_waveOutDevice?.PlaybackState == PlaybackState.Paused)
            {
                _waveOutDevice.Play();
            }
        }

        public void Stop()
        {
            _isBuffering = false;
            _bufferStatusTimer?.Dispose();
            _positionLogTimer?.Dispose();
            _bufferingCancellationTokenSource?.Cancel();

            _waveOutDevice?.Stop();
            _audioFileReader?.Dispose();
            _audioFileReader = null;
            _bufferedWaveProvider = null;
            _volumeProvider = null;
            _currentPosition = TimeSpan.Zero;
            _totalDuration = TimeSpan.Zero;
            _isYouTubeStream = false;
        }

        public void SetVolume(float volume)
        {
            _audioVolume = volume;
            if (_volumeProvider != null)
            {
                _volumeProvider.Volume = _audioVolume;
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

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if ((_audioFileReader != null && _audioFileReader.Position >= _audioFileReader.Length) || 
                (_bufferedWaveProvider != null && _bufferedWaveProvider.BufferedBytes == 0 && !_isBuffering))
            {
                TrackEnded?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task StartYouTubeStreamAsync(string videoId)
        {
            var streamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(videoId);
            var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

            if (streamInfo == null)
            {
                throw new InvalidOperationException("No audio stream found for this video.");
            }

            var stream = await _youtubeClient.Videos.Streams.GetAsync(streamInfo);

            var waveFormat = new WaveFormat(44100, 16, 2);
            _bufferedWaveProvider = new BufferedWaveProvider(waveFormat)
            {
                BufferLength = 8 * 1024 * 1024 // 8MB buffer
            };

            InitializeAudio(_bufferedWaveProvider);

            _isBuffering = true;
            _bufferingCancellationTokenSource = new CancellationTokenSource();

            _streamStartTime = DateTime.Now;
            _currentPosition = TimeSpan.Zero;
            var videoDetails = await _youtubeClient.Videos.GetAsync(videoId);
            TotalDuration = videoDetails.Duration ?? TimeSpan.Zero;

            _ = Task.Run(() => BufferYouTubeStreamAsync(stream, _bufferingCancellationTokenSource.Token));
        }

        private async Task BufferYouTubeStreamAsync(Stream inputStream, CancellationToken cancellationToken)
        {
            try
            {
                using var ffmpegProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = "-i pipe:0 -f s16le -ar 44100 -ac 2 pipe:1",
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                ffmpegProcess.Start();

                _ = Task.Run(async () =>
                {
                    await inputStream.CopyToAsync(ffmpegProcess.StandardInput.BaseStream, BufferSize, cancellationToken);
                    ffmpegProcess.StandardInput.Close();
                }, cancellationToken);

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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in BufferYouTubeStreamAsync: {ex.Message}");
            }
            finally
            {
                _isBuffering = false;
            }
        }

        private void StartLocalFile(string filePath)
        {
            _audioFileReader = new AudioFileReader(filePath);
            InitializeAudio(_audioFileReader);
            TotalDuration = _audioFileReader.TotalTime;
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
                    _waveOutDevice.Pause();
                    _isBuffering = true;
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
            _currentPosition = TimeSpan.Zero;
            _positionLogTimer = new Timer(_ => UpdateCurrentPosition(), null, 0, 100);
        }

        private void UpdateCurrentPosition()
        {
            if (_isYouTubeStream)
            {
                _currentPosition = DateTime.Now - _streamStartTime;
                if (_currentPosition > _totalDuration)
                {
                    _currentPosition = _totalDuration;
                }
            }
            else
            {
                _currentPosition = _audioFileReader?.CurrentTime ?? TimeSpan.Zero;
            }
            Console.WriteLine($"Current position: {_currentPosition}");
        }
    }
}