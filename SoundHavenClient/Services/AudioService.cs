using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SoundHaven.ViewModels;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using YouTubeMusicAPI.Client;
using YouTubeMusicAPI.Models.Streaming;

namespace SoundHaven.Services
{
    public class AudioService : ViewModelBase
    {
        private readonly YoutubeClient _youtubeClient;
        private IYouTubeDownloadService _youTubeDownloadService;
        private bool _isYouTubeStream;
        private IWavePlayer _waveOutDevice;
        private WaveStream _audioFileReader;
        private BufferedWaveProvider _bufferedWaveProvider;
        private VolumeSampleProvider _volumeProvider;
        private float _audioVolume = 1.0f;
        private TimeSpan _totalDuration;

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
        public TimeSpan CurrentPosition => _audioFileReader?.CurrentTime ?? TimeSpan.Zero;
        
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
                }
            }
        }

        public TimeSpan TotalDuration
        {
            get => _totalDuration;
            private set => SetProperty(ref _totalDuration, value);
        }

        public void Restart()
        {
            _audioFileReader.CurrentTime = TimeSpan.Zero;
        }

        public async Task StartAsync(string source, bool isYouTubeVideo = false, TimeSpan startingPosition = default)
        {
            Stop();
            _isYouTubeStream = isYouTubeVideo;

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
                OnPropertyChanged(nameof(CurrentPosition));
            }
        }

        public async Task Pause()
        {
            _waveOutDevice?.Pause();
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task Resume()
        {
            _waveOutDevice.Play();
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Stop()
        {
            _positionLogTimer?.Dispose();
            _waveOutDevice?.Stop();
            _audioFileReader?.Dispose();
            _audioFileReader = null;
            _bufferedWaveProvider = null;
            _volumeProvider = null;
            _isYouTubeStream = false;
            OnPropertyChanged(nameof(CurrentPosition));
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private async Task StartYouTubeStreamAsync(string videoId)
        {
            try
            {
                var youtubeMusicClient = new YouTubeMusicClient();
                var videoDetails = await youtubeMusicClient.GetSongVideoInfoAsync(videoId);
                
                // var videoDetails = await _youtubeClient.Videos.GetAsync(videoId);
                TotalDuration = videoDetails?.Duration ?? TimeSpan.Zero;

                StreamingData streamingData = await youtubeMusicClient.GetStreamingDataAsync(videoId);
                
                foreach (MediaStreamInfo streamInfo in streamingData.StreamInfo)
                {
                    if (streamInfo is AudioStreamInfo audioStreamInfo)
                    {
                        _audioFileReader = new MediaFoundationReader(audioStreamInfo.Url);
                    }
                }

                await InitializeAudio(_audioFileReader);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in StartYouTubeStreamAsync: {ex.Message}");
                throw;
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
                Volume = (float) Math.Pow(_audioVolume, 2)
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
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(CurrentPosition)));
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            var isPlaying = !IsPaused;
            var timeExceeded = (_audioFileReader?.CurrentTime.TotalSeconds >= _audioFileReader?.TotalTime.TotalSeconds - 1);
            var waveProviderExists = _bufferedWaveProvider != null;
            if (isPlaying && timeExceeded || waveProviderExists)
            {
                TrackEnded?.Invoke(this, EventArgs.Empty);
            }
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}