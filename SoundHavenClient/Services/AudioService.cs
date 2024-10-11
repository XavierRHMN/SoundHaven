using ExCSS;
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
using System.IO.Pipes;
using System.Linq;
using System.Text.RegularExpressions;

namespace SoundHaven.Services
{
    public class AudioService : ViewModelBase, IDisposable
    {
        private Process _mpvProcess;
        private bool _shouldRestart = false;
        private readonly YoutubeClient _youtubeClient;
        private TimeSpan _totalPauseTime = TimeSpan.Zero;
        private AudioFileReader _audioFileReader;

        private float _audioVolume = 1.0f;
        private BufferedWaveProvider _bufferedWaveProvider;
        private CancellationTokenSource _bufferingCancellationTokenSource;
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
                    await StartYouTubeStreamAsync(cleanVideoId);
                    StartPositionLogging();
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
                _startTime = position;
                _currentYoutubeTime = position;

                // Send seek command to MPV
                SendMpvCommand("set_property", "time-pos", position.TotalSeconds);
                
                // Reset playback timing
                _playbackStartTime = DateTime.Now;
                _currentPauseStartTime = DateTime.MinValue;
                _totalPauseTime = TimeSpan.Zero;
            }
        }

        public void Pause()
        {
            _waveOutDevice?.Pause();
            
            if (_isYouTubeStream && _mpvProcess != null && !_mpvProcess.HasExited)
            {
                SendMpvCommand("set_property", "pause", true);
            }

            IsPaused = true;
            _currentPauseStartTime = DateTime.Now; // Start tracking pause time
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Resume()
        {
            _waveOutDevice.Play();

            if (_isYouTubeStream && _mpvProcess != null && !_mpvProcess.HasExited)
            {
                SendMpvCommand("set_property", "pause", false);
            }
            
            IsPaused = false;
            _totalPauseTime += DateTime.Now - _currentPauseStartTime; // Update total pause time
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SendMpvCommand(string commandName, params object[] args)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", "mpvsocket", PipeDirection.InOut))
                {
                    client.Connect(1000); // Wait up to 1 second
                    using (var writer = new StreamWriter(client))
                    using (var reader = new StreamReader(client))
                    {
                        var commandObject = new { command = new object[] { commandName }.Concat(args).ToArray() };
                        string jsonCommand = Newtonsoft.Json.JsonConvert.SerializeObject(commandObject);

                        // Log the command being sent
                        Console.WriteLine($"Sending command to MPV: {jsonCommand}");

                        writer.WriteLine(jsonCommand);
                        writer.Flush();

                        // Read and log the response from MPV
                        string response = reader.ReadLine();
                        Console.WriteLine($"Response from MPV: {response}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending command to MPV: {ex.Message}");
            }
        }
        
        public void Stop(bool resetPosition = true)
        {
            _isBuffering = false;
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
        
        private async Task StartYouTubeStreamAsync(string videoId)
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
                _bufferedWaveProvider = new BufferedWaveProvider(waveFormat)
                {
                    BufferLength = 8 * 1024 * 1024 // 8MB buffer
                };

                InitializeAudio(_bufferedWaveProvider);

                _isBuffering = true;
                _bufferingCancellationTokenSource = new CancellationTokenSource();

                var videoDetails = await _youtubeClient.Videos.GetAsync(videoId);
                TotalDuration = videoDetails.Duration ?? TimeSpan.Zero;

                // Start the FFmpeg process once and keep it running
                _ = Task.Run(() => ContinuousBufferYouTubeStreamAsync(streamUrl, _bufferingCancellationTokenSource.Token));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in StartYouTubeStreamAsync: {ex.Message}");
                throw;
            }
        }
        
        private async Task ContinuousBufferYouTubeStreamAsync(string streamUrl, CancellationToken cancellationToken)
        {
            string mpvPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Libraries", "mpv.exe");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_mpvProcess == null || _mpvProcess.HasExited || _shouldRestart)
                    {
                        _mpvProcess?.Kill();
                        _mpvProcess?.Dispose();

                        _mpvProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = mpvPath,
                                Arguments = $"--no-video --no-terminal --no-cache --demuxer-max-bytes=4M --demuxer-max-back-bytes=2M --start={_currentYoutubeTime.TotalSeconds} --input-ipc-server=mpvsocket \"{streamUrl}\"",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            },
                            EnableRaisingEvents = true
                        };


                        _mpvProcess.Exited += (sender, args) => _shouldRestart = true;

                        _mpvProcess.Start();
                        _shouldRestart = false;

                        // Read and log MPV's standard error output
                        _ = Task.Run(() =>
                        {
                            string line;
                            while ((line = _mpvProcess.StandardError.ReadLine()) != null)
                            {
                                Console.WriteLine($"MPV: {line}");
                            }
                        }, cancellationToken);
                    }

                    await Task.Delay(1000, cancellationToken); // Check process state every second
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Buffering operation was canceled.");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in MPV streaming: {ex.Message}");
                    _shouldRestart = true;
                    await Task.Delay(1000, cancellationToken);
                }
            }

            _mpvProcess?.Kill();
            _mpvProcess?.Dispose();
            _mpvProcess = null;
            _isBuffering = false;
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
                Console.WriteLine(_currentYoutubeTime);
                
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
            }
            OnPropertyChanged(nameof(CurrentYoutubePosition));
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            Stop();
            _waveOutDevice?.Dispose();
            _bufferingCancellationTokenSource?.Dispose();
            _positionLogTimer?.Dispose();
            _mpvProcess?.Kill();
            _mpvProcess?.Dispose();
        }
    }
}
