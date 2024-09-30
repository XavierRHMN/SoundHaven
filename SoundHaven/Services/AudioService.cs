using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.IO;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using NAudio.MediaFoundation;
using System.Diagnostics;
using System.IO.Pipes;

namespace SoundHaven.Services
{
    public class AudioService : IDisposable
    {
        private IWavePlayer _waveOutDevice;
        private AudioFileReader _audioFileReader;
        private BufferedWaveProvider _bufferedWaveProvider;
        private VolumeSampleProvider _volumeProvider;
        private float _audioVolume = 0.1f;
        private readonly YoutubeClient _youtubeClient;
        private Task _bufferingTask;
        private const int BUFFER_SIZE = 4 * 1024 * 1024 * 100; // 4MB buffer
        private bool _isBuffering = false;
        private bool _isDisposed = false;
        public event EventHandler TrackEnded;

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

        public bool IsStopped() => _waveOutDevice.PlaybackState == PlaybackState.Stopped;

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if ((_audioFileReader != null && _audioFileReader.Position >= _audioFileReader.Length) ||
                (_bufferedWaveProvider != null && _bufferedWaveProvider.BufferedBytes == 0 && !_isBuffering))
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

        public async Task Start(string source, bool isYouTubeVideo = false)
        {
            Stop();

            try
            {
                if (isYouTubeVideo)
                {
                    await StartYouTubeStream(source);
                }
                else
                {
                    StartLocalFile(source);
                }

                if (_waveOutDevice != null && (_audioFileReader != null || _bufferedWaveProvider != null))
                {
                    _waveOutDevice.Play();
                    StartBufferStatusTimer();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AudioService.Start: {ex.Message}");
                throw;
            }
        }

        private System.Threading.Timer _bufferStatusTimer;

        private void StartBufferStatusTimer()
        {
            _bufferStatusTimer?.Dispose();
            _bufferStatusTimer = new System.Threading.Timer(_ => CheckBufferStatus(), null, 0, 500);
        }

        private async Task StartYouTubeStream(string videoId)
        {
            var streamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(videoId);
            var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

            if (streamInfo == null)
            {
                throw new InvalidOperationException("No audio stream found for this video.");
            }

            var stream = await _youtubeClient.Videos.Streams.GetAsync(streamInfo);

            // Adjust wave format according to the stream's actual format
            var waveFormat = new WaveFormat(44100, 16, 2);
            _bufferedWaveProvider = new BufferedWaveProvider(waveFormat);
            _bufferedWaveProvider.BufferLength = 8 * 1024 * 1024; // 8MB buffer

            InitializeAudio(_bufferedWaveProvider);

            _isBuffering = true;
            _bufferingTask = Task.Run(() => BufferYouTubeStream(stream));
        }

        private async Task BufferYouTubeStream(Stream inputStream)
        {
            try
            {
                var ffmpeg = new Process
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

                ffmpeg.Start();

                // Copy input stream to FFmpeg's standard input
                Task.Run(async () =>
                {
                    await inputStream.CopyToAsync(ffmpeg.StandardInput.BaseStream);
                    ffmpeg.StandardInput.Close();
                });

                byte[] buffer = new byte[BUFFER_SIZE];
                int bytesRead;
                while ((bytesRead = await ffmpeg.StandardOutput.BaseStream.ReadAsync(buffer, 0, buffer.Length)) > 0 && !_isDisposed)
                {
                    _bufferedWaveProvider.AddSamples(buffer, 0, bytesRead);

                    // Throttle if buffer is too full
                    while (_bufferedWaveProvider.BufferedBytes > _bufferedWaveProvider.BufferLength * 0.8 && !_isDisposed)
                    {
                        await Task.Delay(50);
                    }
                }

                ffmpeg.WaitForExit();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in BufferYouTubeStream: {ex.Message}");
            }
        }
        
        

        private void CheckBufferStatus()
        {
            if (_bufferedWaveProvider != null)
            {
                Console.WriteLine($"Buffer status: {_bufferedWaveProvider.BufferedBytes} / {_bufferedWaveProvider.BufferLength} bytes");
                if (_bufferedWaveProvider.BufferedBytes < 0 && !_isBuffering)
                {
                    Console.WriteLine("Buffer underrun detected. Pausing playback.");
                    _waveOutDevice.Pause();
                    _isBuffering = true;
                }
                else if (_bufferedWaveProvider.BufferedBytes > _bufferedWaveProvider.BufferLength * 0.8 && _isBuffering)
                {
                    Console.WriteLine("Buffer refilled. Resuming playback.");
                    _waveOutDevice.Play();
                    _isBuffering = false;
                }
            }
        }
        
        private void StartLocalFile(string filePath)
        {
            _audioFileReader = new AudioFileReader(filePath);
            InitializeAudio(_audioFileReader);
        }

        private void InitializeAudio(IWaveProvider waveProvider)
        {
            _volumeProvider = new VolumeSampleProvider(waveProvider.ToSampleProvider());
            _volumeProvider.Volume = _audioVolume;

            if (_waveOutDevice != null)
            {
                _waveOutDevice.Stop();
                _waveOutDevice.Dispose();
            }
            _waveOutDevice = new WaveOutEvent();
            _waveOutDevice.Init(_volumeProvider);
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
            _isBuffering = false;
            _bufferStatusTimer?.Dispose();
            _waveOutDevice?.Stop();
            _audioFileReader?.Dispose();
            _audioFileReader = null;
            _bufferedWaveProvider = null;
            _volumeProvider = null;
        }

        public float GetCurrentVolume()
        {
            return _audioVolume;
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
            _isDisposed = true;
            Stop();
            _waveOutDevice?.Dispose();
            _bufferingTask?.Dispose();
            _bufferStatusTimer?.Dispose();
        }
    }
}