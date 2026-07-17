using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace SoundHaven.Services;

/// <summary>An audio output device; <c>Id</c> "-1" is the Windows system default.</summary>
public sealed record AudioOutputDevice(string Id, string Name);

public enum PlaybackStatus
{
    Stopped,
    Loading,
    Playing,
    Paused,
    Seeking,
    Failed
}

public abstract record PlaybackSource
{
    private PlaybackSource()
    {
    }

    public sealed record LocalFile(string FilePath) : PlaybackSource;

    public sealed record YouTube(string VideoId) : PlaybackSource;
}

public sealed class PlaybackFailedEventArgs(Exception exception, string userMessage) : EventArgs
{
    public Exception Exception { get; } = exception;

    public string UserMessage { get; } = userMessage;
}

public interface IAudioService : INotifyPropertyChanged, IDisposable
{
    event EventHandler? TrackEnded;

    event EventHandler? PlaybackStateChanged;

    event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

    PlaybackStatus Status { get; }

    bool IsPlaying { get; }

    bool IsPaused { get; }

    bool IsStopped { get; }

    bool IsSeekBuffering { get; }

    TimeSpan CurrentPosition { get; }

    TimeSpan TotalDuration { get; }

    float AudioVolume { get; set; }

    Task StartAsync(
        PlaybackSource source,
        TimeSpan startingPosition = default,
        CancellationToken cancellationToken = default);

    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);

    Task PauseAsync(CancellationToken cancellationToken = default);

    Task ResumeAsync(CancellationToken cancellationToken = default);

    Task RestartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Available audio output devices, "System default" first.</summary>
    IReadOnlyList<AudioOutputDevice> GetOutputDevices();

    /// <summary>Id of the device audio currently routes to ("-1" = system default).</summary>
    string CurrentOutputDeviceId { get; }

    /// <summary>Routes playback to the given device, keeping position and play state.</summary>
    Task SetOutputDeviceAsync(string deviceId, CancellationToken cancellationToken = default);
}
