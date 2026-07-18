using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SoundHaven.ViewModels;

namespace SoundHaven.Services;

public sealed class AudioService : ViewModelBase, IAudioService
{
    private readonly IYouTubeMediaService _youTubeMediaService;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private CancellationTokenSource _sessionCancellation = new();
    private WasapiOut? _outputDevice;
    private MMDevice? _outputMmDevice;
    private WaveStream? _reader;
    private VolumeSampleProvider? _volumeProvider;
    private Timer? _positionTimer;
    private PlaybackSource? _currentSource;
    private PlaybackStatus _status = PlaybackStatus.Stopped;
    private TimeSpan _currentPosition;
    private TimeSpan _totalDuration;
    private float _audioVolume = 0.25f;
    private long _sessionGeneration;
    private string _outputDeviceId = string.Empty;
    private bool _disposed;

    // Empty id == the Windows default playback device.
    private const string DefaultDeviceId = "";

    public AudioService(IYouTubeMediaService youTubeMediaService)
    {
        _youTubeMediaService = youTubeMediaService
            ?? throw new ArgumentNullException(nameof(youTubeMediaService));
    }

    public event EventHandler? TrackEnded;

    public event EventHandler? PlaybackStateChanged;

    public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

    public PlaybackStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(IsPlaying));
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(IsStopped));
                OnPropertyChanged(nameof(IsSeekBuffering));
                PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsPlaying => Status == PlaybackStatus.Playing;

    public bool IsPaused => Status == PlaybackStatus.Paused;

    public bool IsStopped => Status is PlaybackStatus.Stopped or PlaybackStatus.Failed;

    public bool IsSeekBuffering => Status is PlaybackStatus.Loading or PlaybackStatus.Seeking;

    public TimeSpan CurrentPosition
    {
        get => _currentPosition;
        private set => SetProperty(ref _currentPosition, value);
    }

    public TimeSpan TotalDuration
    {
        get => _totalDuration;
        private set => SetProperty(ref _totalDuration, value);
    }

    public float AudioVolume
    {
        get => _audioVolume;
        set
        {
            float clamped = Math.Clamp(value, 0f, 1f);
            if (SetProperty(ref _audioVolume, clamped) && _volumeProvider is not null)
            {
                _volumeProvider.Volume = ToPerceptualVolume(clamped);
            }
        }
    }

    public async Task StartAsync(
        PlaybackSource source,
        TimeSpan startingPosition = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfDisposed();

        long generation = Interlocked.Increment(ref _sessionGeneration);
        var newSessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource oldSessionCancellation = Interlocked.Exchange(
            ref _sessionCancellation,
            newSessionCancellation);
        oldSessionCancellation.Cancel();
        oldSessionCancellation.Dispose();

        CancellationToken sessionToken = newSessionCancellation.Token;
        await _operationLock.WaitAsync(sessionToken);
        try
        {
            sessionToken.ThrowIfCancellationRequested();
            EnsureCurrentGeneration(generation, sessionToken);
            SetStatus(PlaybackStatus.Loading);
            DisposePlaybackCore();
            _currentSource = source;

            try
            {
                ReaderResult result = await CreateReaderAsync(source, startingPosition, sessionToken);
                EnsureCurrentGeneration(generation, sessionToken);

                InitializeOutput(result.Reader);
                TotalDuration = result.Duration;
                CurrentPosition = ClampPosition(startingPosition, result.Duration);
                StartPositionTimer();

                _outputDevice!.Play();
                SetStatus(PlaybackStatus.Playing);
            }
            catch (OperationCanceledException) when (sessionToken.IsCancellationRequested)
            {
                DisposePlaybackCore();
                throw;
            }
            catch (Exception exception)
            {
                DisposePlaybackCore();
                ReportFailure(exception, "SoundHaven could not start this track.");
                throw;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task SeekAsync(
        TimeSpan position,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        PlaybackSource? source = _currentSource;
        if (source is null)
        {
            return;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _sessionCancellation.Token);
        CancellationToken token = linkedCancellation.Token;
        await _operationLock.WaitAsync(token);
        try
        {
            bool shouldResume = IsPlaying;
            TimeSpan target = ClampPosition(position, TotalDuration);
            SetStatus(PlaybackStatus.Seeking);

            try
            {
                DisposePlaybackCore();
                ReaderResult result = await CreateReaderAsync(source, target, token);
                InitializeOutput(result.Reader);
                TotalDuration = result.Duration;
                CurrentPosition = target;
                StartPositionTimer();

                if (shouldResume)
                {
                    _outputDevice!.Play();
                    SetStatus(PlaybackStatus.Playing);
                }
                else
                {
                    SetStatus(PlaybackStatus.Paused);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                DisposePlaybackCore();
                ReportFailure(exception, "SoundHaven could not seek in this track.");
                throw;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            if (_outputDevice?.PlaybackState == PlaybackState.Playing)
            {
                _outputDevice.Pause();
                UpdatePosition();
                SetStatus(PlaybackStatus.Paused);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            if (_outputDevice is not null && Status == PlaybackStatus.Paused)
            {
                _outputDevice.Play();
                SetStatus(PlaybackStatus.Playing);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            if (_reader is null || _volumeProvider is null)
            {
                return;
            }

            // Seek the already-open reader back to the start and rebuild only the
            // output stage — no stream re-resolve, so there is no loading delay.
            bool wasPlaying = _outputDevice?.PlaybackState == PlaybackState.Playing;
            DisposeOutputDevice();
            try
            {
                _reader.CurrentTime = TimeSpan.Zero;
            }
            catch
            {
                // Some readers can't seek precisely; playback still restarts.
            }

            CreateOutputDevice();
            CurrentPosition = TimeSpan.Zero;
            StartPositionTimer();
            if (wasPlaying)
            {
                _outputDevice!.Play();
                SetStatus(PlaybackStatus.Playing);
            }
            else
            {
                SetStatus(PlaybackStatus.Paused);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public IReadOnlyList<AudioOutputDevice> GetOutputDevices()
    {
        var devices = new List<AudioOutputDevice>
        {
            new(DefaultDeviceId, "System default")
        };

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(
                DataFlow.Render,
                DeviceState.Active))
            {
                using (device)
                {
                    devices.Add(new AudioOutputDevice(device.ID, device.FriendlyName));
                }
            }
        }
        catch
        {
            // Fall back to just the system default if enumeration fails.
        }

        return devices;
    }

    public string CurrentOutputDeviceId => _outputDeviceId;

    public async Task SetOutputDeviceAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string normalizedId = deviceId ?? DefaultDeviceId;

        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            if (string.Equals(normalizedId, _outputDeviceId, StringComparison.Ordinal))
            {
                return;
            }

            _outputDeviceId = normalizedId;

            // Rebuild only the output stage on the new device, preserving the
            // reader position and whether we were playing.
            if (_volumeProvider is null || _reader is null)
            {
                return;
            }

            bool wasPlaying = _outputDevice?.PlaybackState == PlaybackState.Playing;
            DisposeOutputDevice();
            try
            {
                CreateOutputDevice();
                if (wasPlaying)
                {
                    _outputDevice!.Play();
                }
            }
            catch (Exception exception)
            {
                ReportFailure(exception, "SoundHaven could not switch the audio output device.");
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _sessionGeneration);
        CancellationTokenSource replacement = new();
        CancellationTokenSource previous = Interlocked.Exchange(
            ref _sessionCancellation,
            replacement);
        previous.Cancel();
        previous.Dispose();

        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            DisposePlaybackCore();
            _currentSource = null;
            CurrentPosition = TimeSpan.Zero;
            TotalDuration = TimeSpan.Zero;
            SetStatus(PlaybackStatus.Stopped);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Interlocked.Increment(ref _sessionGeneration);
        _sessionCancellation.Cancel();
        _positionTimer?.Dispose();
        _positionTimer = null;
        DisposePlaybackCore();
        _sessionCancellation.Dispose();
        _operationLock.Dispose();
        base.Dispose();
    }

    private async Task<ReaderResult> CreateReaderAsync(
        PlaybackSource source,
        TimeSpan position,
        CancellationToken cancellationToken)
    {
        return source switch
        {
            PlaybackSource.LocalFile local => await CreateLocalReaderAsync(
                local.FilePath,
                position,
                cancellationToken),
            PlaybackSource.YouTube youTube => await CreateYouTubeReaderAsync(
                youTube.VideoId,
                position,
                cancellationToken),
            _ => throw new NotSupportedException($"Unsupported playback source: {source.GetType().Name}")
        };
    }

    private static async Task<ReaderResult> CreateLocalReaderAsync(
        string filePath,
        TimeSpan position,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("The selected audio file no longer exists.", filePath);
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            WaveStream reader = CreateFileReader(filePath);
            try
            {
                TimeSpan duration = reader.TotalTime;
                reader.CurrentTime = ClampPosition(position, duration);
                return new ReaderResult(reader, duration);
            }
            catch
            {
                reader.Dispose();
                throw;
            }
        }, cancellationToken);
    }

    private async Task<ReaderResult> CreateYouTubeReaderAsync(
        string videoId,
        TimeSpan position,
        CancellationToken cancellationToken)
    {
        // Previously played (or prefetched) tracks live in the on-disk cache;
        // opening that file skips both stream resolution and the HTTPS probe.
        string? preCachedPath = _youTubeMediaService.TryGetCachedAudioPath(videoId);
        if (preCachedPath is not null)
        {
            return await CreateLocalReaderAsync(preCachedPath, position, cancellationToken);
        }

        YouTubeStreamSource stream = await _youTubeMediaService.ResolveStreamAsync(
            videoId,
            cancellationToken);

        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reader = new MediaFoundationReader(stream.StreamUri.AbsoluteUri);
                try
                {
                    TimeSpan duration = stream.Duration > TimeSpan.Zero
                        ? stream.Duration
                        : reader.TotalTime;
                    reader.CurrentTime = ClampPosition(position, duration);
                    return new ReaderResult(reader, duration);
                }
                catch
                {
                    reader.Dispose();
                    throw;
                }
            }, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            string cachedPath = await _youTubeMediaService.CacheAudioAsync(
                videoId,
                cancellationToken: cancellationToken);
            return await CreateLocalReaderAsync(cachedPath, position, cancellationToken);
        }
    }

    private static WaveStream CreateFileReader(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        return extension.ToLowerInvariant() switch
        {
            ".mp3" or ".wav" or ".aiff" or ".aif" => new AudioFileReader(filePath),
            _ => new MediaFoundationReader(filePath)
        };
    }

    private void InitializeOutput(WaveStream reader)
    {
        _reader = reader;
        _volumeProvider = new VolumeSampleProvider(reader.ToSampleProvider())
        {
            Volume = ToPerceptualVolume(AudioVolume)
        };

        CreateOutputDevice();
    }

    // WASAPI shared mode renders silence when the decoder can't feed it in time
    // (network stall, teardown). DirectSoundOut's looping ring buffer instead
    // replays its stale contents, which is audible as a rapid stutter of the last
    // ~150ms whenever playback ends, skips, or the stream hiccups.
    private void CreateOutputDevice()
    {
        MMDevice device = ResolveOutputDevice();
        _outputMmDevice?.Dispose();
        _outputMmDevice = device;

        _outputDevice = new WasapiOut(device, AudioClientShareMode.Shared, true, 150);
        _outputDevice.PlaybackStopped += OnPlaybackStopped;
        _outputDevice.Init(new LeadOutSampleProvider(_volumeProvider!));
    }

    private MMDevice ResolveOutputDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!string.IsNullOrEmpty(_outputDeviceId))
        {
            try
            {
                MMDevice selected = enumerator.GetDevice(_outputDeviceId);
                if (selected.State == DeviceState.Active)
                {
                    return selected;
                }

                selected.Dispose();
            }
            catch
            {
                // The chosen device is gone; fall back to the system default.
            }
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    private void DisposeOutputDevice()
    {
        if (_outputDevice is not null)
        {
            _outputDevice.PlaybackStopped -= OnPlaybackStopped;
            try
            {
                _outputDevice.Stop();
            }
            catch
            {
                // Device teardown is best effort; disposal still needs to continue.
            }

            _outputDevice.Dispose();
            _outputDevice = null;
        }

        _outputMmDevice?.Dispose();
        _outputMmDevice = null;
    }

    private void DisposePlaybackCore()
    {
        _positionTimer?.Dispose();
        _positionTimer = null;

        DisposeOutputDevice();

        _reader?.Dispose();
        _reader = null;
        _volumeProvider = null;
    }

    private void StartPositionTimer()
    {
        _positionTimer?.Dispose();
        _positionTimer = new Timer(
            _ => UpdatePosition(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(250));
    }

    private void UpdatePosition()
    {
        WaveStream? reader = _reader;
        if (reader is null)
        {
            return;
        }

        TimeSpan position;
        try
        {
            position = reader.CurrentTime;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        RunOnUiThread(() => CurrentPosition = ClampPosition(position, TotalDuration));
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (_disposed || sender != _outputDevice)
        {
            return;
        }

        if (eventArgs.Exception is not null)
        {
            ReportFailure(eventArgs.Exception, "Audio playback stopped because the output device failed.");
            return;
        }

        TimeSpan finalPosition;
        try
        {
            finalPosition = _reader?.CurrentTime ?? CurrentPosition;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        bool reachedEnd = TotalDuration > TimeSpan.Zero
            && finalPosition >= TotalDuration - TimeSpan.FromSeconds(1);
        if (Status == PlaybackStatus.Playing && reachedEnd)
        {
            RunOnUiThread(() =>
            {
                CurrentPosition = ClampPosition(finalPosition, TotalDuration);
                SetStatusCore(PlaybackStatus.Stopped);
                TrackEnded?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    private void ReportFailure(Exception exception, string userMessage)
    {
        RunOnUiThread(() =>
        {
            SetStatusCore(PlaybackStatus.Failed);
            PlaybackFailed?.Invoke(this, new PlaybackFailedEventArgs(exception, userMessage));
        });
    }

    private void SetStatus(PlaybackStatus value)
    {
        RunOnUiThread(() => SetStatusCore(value));
    }

    private void SetStatusCore(PlaybackStatus value)
    {
        Status = value;
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private void EnsureCurrentGeneration(long generation, CancellationToken cancellationToken)
    {
        if (generation != Volatile.Read(ref _sessionGeneration))
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static TimeSpan ClampPosition(TimeSpan position, TimeSpan duration)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (duration > TimeSpan.Zero && position > duration)
        {
            return duration;
        }

        return position;
    }

    private static float ToPerceptualVolume(float volume)
    {
        return volume * volume;
    }

    private sealed record ReaderResult(WaveStream Reader, TimeSpan Duration);

    // DirectSoundOut's ring buffer keeps cycling while its end-of-stream stop
    // propagates, audibly repeating the last ~150ms of every track right before
    // the next one starts. Feeding a short silent lead-out once the source is
    // exhausted means whatever loops during teardown is silence.
    private sealed class LeadOutSampleProvider : ISampleProvider
    {
        private static readonly TimeSpan LeadOutDuration = TimeSpan.FromMilliseconds(400);
        private readonly ISampleProvider _source;
        private int _remainingSilenceSamples;
        private bool _sourceEnded;

        public LeadOutSampleProvider(ISampleProvider source)
        {
            _source = source;
            _remainingSilenceSamples =
                (int)(LeadOutDuration.TotalSeconds * source.WaveFormat.SampleRate)
                * source.WaveFormat.Channels;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            if (!_sourceEnded)
            {
                int read = _source.Read(buffer, offset, count);
                if (read > 0)
                {
                    return read;
                }

                _sourceEnded = true;
            }

            int silence = Math.Min(count, _remainingSilenceSamples);
            if (silence <= 0)
            {
                return 0;
            }

            Array.Clear(buffer, offset, silence);
            _remainingSilenceSamples -= silence;
            return silence;
        }
    }
}
