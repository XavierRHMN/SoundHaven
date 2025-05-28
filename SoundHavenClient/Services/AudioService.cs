using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Newtonsoft.Json;
using SoundHaven.ViewModels;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace SoundHaven.Services
{
    public class AudioService : ViewModelBase, IDisposable
    {
        private Process _mpvProcess;
        private readonly YoutubeClient _youtubeClient;
        private IYouTubeDownloadService _youTubeDownloadService;
        private bool _isYouTubeStream;
        private IWavePlayer _waveOutDevice;
        private AudioFileReader _audioFileReader;
        private BufferedWaveProvider _bufferedWaveProvider;
        private VolumeSampleProvider _volumeProvider;
        private float _audioVolume = 1.0f;
        private TimeSpan _currentYoutubeTime;
        private TimeSpan _totalDuration;
        private NamedPipeClientStream _mpvPipeClient;
        private StreamWriter _mpvPipeWriter;
        private StreamReader _mpvPipeReader;
        private int _mpvRequestId;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<dynamic>> _mpvResponseHandlers = new();
        private string _currentStreamUrl;
        private CancellationTokenSource _bufferingCancellationTokenSource;
        
        public event EventHandler TrackEnded;
        public event EventHandler PlaybackStateChanged;
        private Timer _positionLogTimer;

        public AudioService()
        {
            _waveOutDevice = new WaveOutEvent();
            _waveOutDevice.PlaybackStopped += OnPlaybackStopped;
            _youtubeClient = new YoutubeClient();
            _youTubeDownloadService = new YouTubeDownloadService();
        }

        public bool IsPlaying => _waveOutDevice?.PlaybackState == PlaybackState.Playing;
        public bool IsPaused => _waveOutDevice?.PlaybackState == PlaybackState.Paused;
        public bool IsStopped => _waveOutDevice?.PlaybackState == PlaybackState.Stopped;
        public TimeSpan CurrentLocalPosition => _audioFileReader?.CurrentTime ?? TimeSpan.Zero;
        public TimeSpan CurrentYoutubePosition => _currentYoutubeTime;
        public bool IsSeekBuffering => false;
        
        public float AudioVolume
        {
            get => _audioVolume;
            set
            {
                if (SetProperty(ref _audioVolume, value))
                {
                    _audioVolume = Math.Clamp(value, 0f, 1f);
                    if (!_isYouTubeStream) _audioVolume *= 0.75f;
                    if (_volumeProvider != null) _volumeProvider.Volume = (float)Math.Pow(_audioVolume, 2);
                    if (_isYouTubeStream && _mpvProcess != null && !_mpvProcess.HasExited)
                        SetMpvVolume(_audioVolume);
                }
            }
        }

        public TimeSpan TotalDuration
        {
            get => _totalDuration;
            private set => SetProperty(ref _totalDuration, value);
        }

        public async Task StartAsync(string source, bool isYouTubeVideo = false, TimeSpan startingPosition = default)
        {
            Stop();
            _isYouTubeStream = isYouTubeVideo;
            _currentYoutubeTime = startingPosition;
            _currentStreamUrl = source;

            try
            {
                if (isYouTubeVideo)
                {
                    await StartYouTubeStreamAsync(_youTubeDownloadService.CleanVideoId(source));
                    await StartPositionLogging();
                }
                else
                {
                    await StartLocalFile(source);
                    await Seek(startingPosition);
                }

                if (_waveOutDevice != null && (_audioFileReader != null || _bufferedWaveProvider != null))
                {
                    _waveOutDevice.Play();
                    PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in StartAsync: {ex.Message}");
                throw;
            }
            OnPlaybackStateChanged();
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

        public async Task Seek(TimeSpan position)
        {
            if (_audioFileReader != null)
            {
                _audioFileReader.CurrentTime = position;
                OnPropertyChanged(nameof(CurrentYoutubePosition));
            }
            else if (_isYouTubeStream)
            {
                _currentYoutubeTime = position;
                await SendMpvCommandAsync("set_property", "time-pos", position.TotalSeconds);
            }
        }

        public async Task Pause()
        {
            _waveOutDevice?.Pause();
            if (_isYouTubeStream && _mpvProcess != null && !_mpvProcess.HasExited)
                await SendMpvCommandAsync("set_property", "pause", true);
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task Resume()
        {
            _waveOutDevice.Play();

            if (_isYouTubeStream)
            {
                if (_mpvProcess != null && !_mpvProcess.HasExited)
                {
                    await SendMpvCommandAsync("set_property", "pause", false);
                }
                else
                {
                    await StartAsync(_currentStreamUrl, _isYouTubeStream);
                }
            }
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Stop()
        {
            _positionLogTimer?.Dispose();
            _bufferingCancellationTokenSource?.Cancel();  
            _waveOutDevice?.Stop();
            _audioFileReader?.Dispose();
            _audioFileReader = null;
            _bufferedWaveProvider = null;
            _volumeProvider = null;
            _isYouTubeStream = false;
            OnPropertyChanged(nameof(CurrentYoutubePosition));
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private async Task StartYouTubeStreamAsync(string videoId)
        {
            
            var streamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(videoId);
            var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();
            if (streamInfo == null) throw new InvalidOperationException("No audio stream found.");
            
            var url = streamInfo.Url;
            using(var mf = new MediaFoundationReader(url))
            using(var wo = new WasapiOut())
            {
                wo.Init(mf);
                wo.Play();
                while (wo.PlaybackState == PlaybackState.Playing)
                {
                    Thread.Sleep(1000);
                }
            }

            // _bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(44100, 16, 2));
            //
            // await InitializeAudio(_bufferedWaveProvider);
            // _bufferingCancellationTokenSource = new CancellationTokenSource(); 
            // var videoDetails = await _youtubeClient.Videos.GetAsync(videoId);
            // TotalDuration = videoDetails.Duration ?? TimeSpan.Zero;
            // await StartAndUpdateMpvProcess(streamInfo.Url, _bufferingCancellationTokenSource.Token);
        }
        
        // Added this method back
        private async Task StartAndUpdateMpvProcess(string streamUrl, CancellationToken cancellationToken)
        {
            await StartMpvProcess(streamUrl);
    
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

        private async Task StartMpvProcess(string streamUrl)
        {
            string mpvPath = Path.Combine(AppContext.BaseDirectory, "mpv.exe");

            _mpvProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = mpvPath,
                    Arguments = $"--no-video --no-terminal --no-cache --demuxer-max-bytes=4M --demuxer-max-back-bytes=2M --start={_currentYoutubeTime.TotalSeconds} --volume={_audioVolume * 100} --input-ipc-server=mpvsocket \"{streamUrl}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };
            _mpvProcess.Exited += OnMpvProcessExited;
            _mpvProcess.Start();
            await InitializeMpvIpcAsync();
        }

        private async Task InitializeMpvIpcAsync()
        {
            try
            {
                _mpvPipeClient = new NamedPipeClientStream(".", "mpvsocket", PipeDirection.InOut, PipeOptions.Asynchronous);
                await _mpvPipeClient.ConnectAsync(500);
                _mpvPipeWriter = new StreamWriter(_mpvPipeClient) { AutoFlush = true };
                _mpvPipeReader = new StreamReader(_mpvPipeClient);
                _ = Task.Run(ReadMpvResponses);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing MPV IPC: {ex.Message}");
            }
        }

        public async Task<dynamic> SendMpvCommandAsync(string commandName, params object[] args)
        {
            try
            {
                int requestId = Interlocked.Increment(ref _mpvRequestId);
                var tcs = new TaskCompletionSource<dynamic>();
                _mpvResponseHandlers[requestId] = tcs;
                await _mpvPipeWriter.WriteLineAsync(JsonConvert.SerializeObject(new { 
                    command = new object[] { commandName }.Concat(args).ToArray(), 
                    request_id = requestId 
                }));
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending MPV command: {ex.Message}");
                return null;
            }
        }

        private void ReadMpvResponses()
        {
            try
            {
                while (_mpvPipeClient?.IsConnected == true)
                {
                    string response = _mpvPipeReader.ReadLine();
                    if (response != null)
                    {
                        var responseObject = JsonConvert.DeserializeObject<dynamic>(response);
                        if (responseObject != null && responseObject.request_id != null)
                        {
                            TaskCompletionSource<dynamic> tcs;
                            if (_mpvResponseHandlers.TryRemove((int)responseObject.request_id, out tcs))
                            {
                                if (responseObject.error == "success")
                                {
                                    tcs.SetResult(responseObject.data);
                                }
                                else
                                {
                                    tcs.SetException(new Exception($"MPV error: {responseObject.error}"));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading MPV responses: {ex.Message}");
            }
        }

        private async Task StartLocalFile(string filePath)
        {
            _audioFileReader = new AudioFileReader(filePath);
            var silence = new OffsetSampleProvider(new SilenceProvider(_audioFileReader.WaveFormat).ToSampleProvider()) 
            { 
                Take = TimeSpan.FromSeconds(0.5) 
            };
            await InitializeAudio(new ConcatenatingSampleProvider(new[] { silence, _audioFileReader.ToSampleProvider() }).ToWaveProvider());
            TotalDuration = _audioFileReader.TotalTime + TimeSpan.FromSeconds(0.5);
        }

        private async Task InitializeAudio(IWaveProvider waveProvider)
        {
            _volumeProvider = new VolumeSampleProvider(waveProvider.ToSampleProvider()) 
            { 
                Volume = (float)Math.Pow(_audioVolume, 2) 
            };
            _waveOutDevice.Init(_volumeProvider);
        }

        private async Task StartPositionLogging()
        {
            _positionLogTimer?.Dispose();
            _positionLogTimer = new Timer(_ => UpdateCurrentPosition(), null, 0, 100);
        }

        private void UpdateCurrentPosition()
        {
            if (_isYouTubeStream) GetCurrentPositionAsync();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(CurrentYoutubePosition)));
        }

        private async void GetCurrentPositionAsync()
        {
            try
            {
                _currentYoutubeTime = TimeSpan.FromSeconds(await GetMpvPropertyAsync("time-pos"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating position: {ex.Message}");
            }
        }

        private async Task<double> GetMpvPropertyAsync(string propertyName)
        {
            try
            {
                return (double)await SendMpvCommandAsync("get_property", propertyName);
            }
            catch
            {
                return 0;
            }
        }

        private async void SetMpvVolume(float volume) => 
            await SendMpvCommandAsync("set_property", "volume", (int)(volume * 100));

        private void OnMpvProcessExited(object sender, EventArgs e)
        {
            _mpvProcess.Dispose();
            _mpvProcess = null;
            _waveOutDevice.Stop();
            _mpvPipeClient?.Dispose();
            _mpvPipeClient = null;
            _mpvPipeWriter = null;
            _mpvPipeReader = null;
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            var isPlaying = !IsPaused;
            var timeExceeded = (_audioFileReader?.CurrentTime.TotalSeconds >= _audioFileReader?.TotalTime.TotalSeconds - 1 || _currentYoutubeTime >= TotalDuration);
            var waveProviderExists = _bufferedWaveProvider != null;
            if (isPlaying && timeExceeded || waveProviderExists)
            {
                TrackEnded?.Invoke(this, EventArgs.Empty);
            }
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public override void Dispose()
        {
            Stop();
            _waveOutDevice?.Dispose();
            _positionLogTimer?.Dispose();
            _bufferingCancellationTokenSource?.Dispose();  // Added this line
            if (_mpvProcess != null && !_mpvProcess.HasExited) _mpvProcess.Kill();
            _mpvProcess?.Dispose();
            _mpvPipeClient?.Dispose();
        }
    }
}