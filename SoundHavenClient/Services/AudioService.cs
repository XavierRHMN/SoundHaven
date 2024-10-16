﻿using ExCSS;
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
using TagLib.Matroska;

namespace SoundHaven.Services
{
    public class AudioService : ViewModelBase, IDisposable
    {
        // YouTube and Process-related
        private Process _mpvProcess;
        private readonly YoutubeClient _youtubeClient;
        private IYouTubeDownloadService _youTubeDownloadService;
        private bool _isYouTubeStream;

        // Audio playback and control
        private IWavePlayer _waveOutDevice;
        private AudioFileReader _audioFileReader;
        private BufferedWaveProvider _bufferedWaveProvider;
        private VolumeSampleProvider _volumeProvider;
        private float _audioVolume = 1.0f;

        // Playback state
        private bool _isSeekBuffering;
        private CancellationTokenSource _bufferingCancellationTokenSource;

        // Time tracking
        private TimeSpan _currentYoutubeTime;
        private TimeSpan _totalPauseTime = TimeSpan.Zero;
        private DateTime? _currentPauseStartTime;
        private TimeSpan _startTime;
        private DateTime _playbackStartTime;
        private TimeSpan _totalDuration;
        
        // Events
        public event EventHandler TrackEnded;
        public event EventHandler PlaybackStateChanged;

        // Timer
        private Timer _positionLogTimer;

        public AudioService()
        {
            _waveOutDevice = new WaveOutEvent();
            _waveOutDevice.PlaybackStopped += OnPlaybackStopped;
            _youtubeClient = new YoutubeClient();
            _youTubeDownloadService = new YouTubeDownloadService();
        }

        public bool IsPaused
        {
            get => _waveOutDevice?.PlaybackState == PlaybackState.Paused;
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
                    if (!_isYouTubeStream) _audioVolume *= 0.75f;
                    if (_volumeProvider != null)
                    {
                        _volumeProvider.Volume = (float)Math.Pow(_audioVolume, 2);
                    }
                    if (_isYouTubeStream && _mpvProcess != null && !_mpvProcess.HasExited)
                    {
                        SetMpvVolume(_audioVolume);
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
            Stop();
            
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
                    Seek(startingPosition);
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
            _currentPauseStartTime = null; // Reset pause time on seek

            if (_audioFileReader != null)
            {
                _audioFileReader.CurrentTime = position;
                OnPropertyChanged(nameof(CurrentYoutubePosition));
            }
            else if (_isYouTubeStream)
            {
                _startTime = position;
                _currentYoutubeTime = position;

                if (IsPaused)
                {
                    _currentPauseStartTime = DateTime.Now;
                }
                else
                {
                    _currentPauseStartTime = null;
                }
                
                // Send seek command to MPV
                SendMpvCommand("set_property", "time-pos", position.TotalSeconds);

                // Reset playback timing
                _playbackStartTime = DateTime.Now;
                _totalPauseTime = TimeSpan.Zero;
            }
        }
        
        public void Pause()
        {
            _waveOutDevice?.Pause();
            _currentPauseStartTime = DateTime.Now;

            if (_isYouTubeStream && _mpvProcess != null && !_mpvProcess.HasExited)
            {
                SendMpvCommand("set_property", "pause", true);
            }

            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Resume()
        {
            _waveOutDevice.Play();

            if (_currentPauseStartTime.HasValue)
            {
                _totalPauseTime += DateTime.Now - _currentPauseStartTime.Value;
                _currentPauseStartTime = null; // Reset after resuming
            }

            if (_isYouTubeStream)
            {
                if (_mpvProcess != null && !_mpvProcess.HasExited)
                {
                    SendMpvCommand("set_property", "pause", false);
                }
            }
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SendMpvCommand(string commandName, params object[] args)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", "mpvsocket", PipeDirection.InOut))
                {
                    if (commandName != "pause") client.Connect(1000); 

                    using (var writer = new StreamWriter(client))
                    {
                        var commandObject = new { command = new object[] { commandName }.Concat(args).ToArray() };
                        string jsonCommand = Newtonsoft.Json.JsonConvert.SerializeObject(commandObject);

                        writer.WriteLine(jsonCommand);
                        writer.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending command to MPV: {ex.Message}");
            }
        }
        
        private void SetMpvVolume(float volume)
        {
            // MPV volume range is 0-100, so we multiply our 0-1 range by 100
            int mpvVolume = (int)(volume * 100);
            SendMpvCommand("set_property", "volume", mpvVolume);
        }
        
        public void Stop()
        {
            _positionLogTimer?.Dispose();
            _bufferingCancellationTokenSource?.Cancel();

            _totalPauseTime = TimeSpan.Zero;

            _waveOutDevice?.Stop();
            _audioFileReader?.Dispose();
            _audioFileReader = null;
            _bufferedWaveProvider = null;
            _volumeProvider = null;
            _isYouTubeStream = false;

            OnPropertyChanged(nameof(CurrentYoutubePosition));
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (IsPaused) return;
            
            if (_audioFileReader != null 
                && _audioFileReader.CurrentTime.TotalSeconds + 5 >= _audioFileReader.TotalTime.TotalSeconds 
                || _bufferedWaveProvider != null || _currentYoutubeTime >= TotalDuration)
            {
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

                _bufferingCancellationTokenSource = new CancellationTokenSource();

                var videoDetails = await _youtubeClient.Videos.GetAsync(videoId);
                TotalDuration = videoDetails.Duration ?? TimeSpan.Zero;

                // Start the FFmpeg process once and keep it running
               StartAndUpdateMpvProcess(streamUrl, _bufferingCancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in StartYouTubeStreamAsync: {ex.Message}");
                throw;
            }
        }
        
        private void StartMpvProcess(string streamUrl)
        {
            string mpvPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Binaries", "mpv", "mpv.exe");

            _mpvProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = mpvPath,
                    Arguments = $"--no-video --no-terminal --no-cache --demuxer-max-bytes=4M --demuxer-max-back-bytes=2M --start={_currentYoutubeTime.TotalSeconds} --volume={_audioVolume * 100} --input-ipc-server=mpvsocket \"{streamUrl}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            _mpvProcess.Exited += OnMpvProcessExited;
            _mpvProcess.Start();
        }

        private void OnMpvProcessExited(object sender, EventArgs e)
        {
            _mpvProcess.Exited -= OnMpvProcessExited;

            // Cleanup
            _mpvProcess.Dispose();
            _mpvProcess = null;
        }

        private void StartAndUpdateMpvProcess(string streamUrl, CancellationToken cancellationToken)
        {
            StartMpvProcess(streamUrl);

            cancellationToken.Register(() =>
            {
                if (_mpvProcess != null && !_mpvProcess.HasExited)
                {
                    _mpvProcess.Kill();
                    _mpvProcess.Dispose();
                    _mpvProcess = null;
                }
            });
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
                TimeSpan currentTotalPauseTime = _totalPauseTime;

                if (IsPaused && _currentPauseStartTime.HasValue)
                {
                    currentTotalPauseTime += DateTime.Now - _currentPauseStartTime.Value;
                }

                TimeSpan totalElapsedPlaybackTime = DateTime.Now - _playbackStartTime - currentTotalPauseTime;
                _currentYoutubeTime = _startTime + totalElapsedPlaybackTime;

                if (_currentYoutubeTime > TotalDuration) Stop();

                Debug.WriteLine($"Current YouTube Time: {_currentYoutubeTime}");
            }
            OnPropertyChanged(nameof(CurrentYoutubePosition));
        }       
        
        public override void Dispose()
        {
            Stop();
            _waveOutDevice?.Dispose();
            _bufferingCancellationTokenSource?.Dispose();
            _positionLogTimer?.Dispose();
            _mpvProcess?.Dispose();
        }
    }
}
