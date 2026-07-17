using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.Data.Sqlite;
using SoundHaven.Data;
using SoundHaven.Models;
using SoundHaven.Services;
using SoundHaven.ViewModels;

namespace SoundHaven.Tests;

public sealed class PlaybackViewModelTests : IDisposable
{
    private readonly string _directory;
    private readonly ThemesViewModel _themes;

    public PlaybackViewModelTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "SoundHaven.Tests",
            Guid.NewGuid().ToString("N"));
        _themes = new ThemesViewModel(
            new AppDatabase(Path.Combine(_directory, "playback.db")));
    }

    [Fact]
    public async Task RepeatAll_AdvancesAndWrapsToFirstTrack()
    {
        var repeat = new RepeatViewModel { RepeatMode = RepeatMode.All };
        var audio = new FakeAudioService();
        using PlaybackViewModel viewModel = CreateViewModel(audio, repeat);
        Song first = CreateYouTubeSong("First", "jNQXAC9IVRw");
        Song last = CreateYouTubeSong("Last", "dQw4w9WgXcQ");
        viewModel.CurrentPlaylist = CreatePlaylist(first, last);
        viewModel.CurrentSong = last;

        audio.RaiseTrackEnded();
        await WaitUntilAsync(() => audio.StartedSources.Count == 1);

        var source = Assert.IsType<PlaybackSource.YouTube>(
            Assert.Single(audio.StartedSources));
        Assert.Equal(first.VideoId, source.VideoId);
        Assert.Same(first, viewModel.CurrentSong);
    }

    [Fact]
    public async Task RepeatOff_StopsAtEndOfPlaylist()
    {
        var repeat = new RepeatViewModel { RepeatMode = RepeatMode.Off };
        var audio = new FakeAudioService();
        using PlaybackViewModel viewModel = CreateViewModel(audio, repeat);
        Song only = CreateYouTubeSong("Only", "jNQXAC9IVRw");
        viewModel.CurrentPlaylist = CreatePlaylist(only);
        viewModel.CurrentSong = only;

        audio.RaiseTrackEnded();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Empty(audio.StartedSources);
        Assert.Same(only, viewModel.CurrentSong);
    }

    [Fact]
    public async Task RepeatOne_ReplaysCurrentTrackWithoutChangingMode()
    {
        var repeat = new RepeatViewModel { RepeatMode = RepeatMode.One };
        var audio = new FakeAudioService();
        using PlaybackViewModel viewModel = CreateViewModel(audio, repeat);
        Song only = CreateYouTubeSong("Only", "jNQXAC9IVRw");
        viewModel.CurrentPlaylist = CreatePlaylist(only);
        viewModel.CurrentSong = only;

        audio.RaiseTrackEnded();
        await WaitUntilAsync(() => audio.StartedSources.Count == 1);

        Assert.Equal(RepeatMode.One, repeat.RepeatMode);
        Assert.Same(only, viewModel.CurrentSong);
    }

    [Fact]
    public async Task PlayFromBeginning_ExposesLoadingTransitionUntilAudioStarts()
    {
        var repeat = new RepeatViewModel();
        var audio = new FakeAudioService
        {
            StartGate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };
        using PlaybackViewModel viewModel = CreateViewModel(audio, repeat);
        Song song = CreateYouTubeSong("Loading", "jNQXAC9IVRw");

        Task playTask = viewModel.PlayFromBeginning(song);
        await WaitUntilAsync(() => audio.Status == PlaybackStatus.Loading);

        Assert.True(viewModel.IsTransitioningTracks);
        Assert.False(viewModel.CanPlaybackControl);
        Assert.Same(song, viewModel.CurrentSong);

        audio.StartGate.SetResult(true);
        await playTask;

        Assert.Equal(PlaybackStatus.Playing, audio.Status);
        Assert.False(viewModel.IsTransitioningTracks);
        Assert.True(viewModel.CanPlaybackControl);
        Assert.Same(song, viewModel.CurrentSong);
    }

    [Fact]
    public async Task PlayShuffledAsync_QueuesRemainingSongsAfterRandomStart()
    {
        var repeat = new RepeatViewModel { RepeatMode = RepeatMode.Off };
        var audio = new FakeAudioService();
        using PlaybackViewModel viewModel = CreateViewModel(audio, repeat);
        Song first = CreateYouTubeSong("First", "jNQXAC9IVRw");
        Song second = CreateYouTubeSong("Second", "dQw4w9WgXcQ");
        Song third = CreateYouTubeSong("Third", "9bZkp7q19f0");
        var sourceOrder = new[] { first, second, third };
        Playlist sourcePlaylist = CreatePlaylist(first, second, third);

        await viewModel.PlayShuffledAsync(sourcePlaylist.Songs, sourcePlaylist.Name);

        Assert.False(viewModel.IsShuffleEnabled);
        Assert.Contains(viewModel.CurrentSong, sourceOrder);
        Assert.Equal(2, viewModel.GetUpcomingSongs().Count);

        var playedAndQueued = new[] { viewModel.CurrentSong! }
            .Concat(viewModel.GetUpcomingSongs())
            .ToList();
        Assert.Equal(3, playedAndQueued.Count);
        Assert.Equal(sourceOrder.OrderBy(song => song.Title), playedAndQueued.OrderBy(song => song.Title));

        // Source playlist order must stay intact.
        Assert.Same(first, sourcePlaylist.Songs[0]);
        Assert.Same(second, sourcePlaylist.Songs[1]);
        Assert.Same(third, sourcePlaylist.Songs[2]);
        Assert.NotSame(sourcePlaylist, viewModel.CurrentPlaylist);
    }

    [Fact]
    public async Task NextTrack_AtEndOfQueue_OnlyWrapsOnRepeatAll()
    {
        var repeat = new RepeatViewModel { RepeatMode = RepeatMode.Off };
        var audio = new FakeAudioService();
        using PlaybackViewModel viewModel = CreateViewModel(audio, repeat);
        Song first = CreateYouTubeSong("First", "jNQXAC9IVRw");
        Song last = CreateYouTubeSong("Last", "dQw4w9WgXcQ");
        viewModel.CurrentPlaylist = CreatePlaylist(first, last);
        viewModel.CurrentSong = last;

        await viewModel.NextTrack();

        Assert.Same(last, viewModel.CurrentSong);
        Assert.Empty(audio.StartedSources);

        repeat.RepeatMode = RepeatMode.All;
        await viewModel.NextTrack();

        Assert.Same(first, viewModel.CurrentSong);
    }

    [Fact]
    public async Task ShuffleNavigation_NeverReplaysCurrentTrackWhenAlternativesExist()
    {
        var repeat = new RepeatViewModel();
        var audio = new FakeAudioService();
        using PlaybackViewModel viewModel = CreateViewModel(audio, repeat);
        Song first = CreateYouTubeSong("First", "jNQXAC9IVRw");
        Song second = CreateYouTubeSong("Second", "dQw4w9WgXcQ");
        viewModel.CurrentPlaylist = CreatePlaylist(first, second);
        viewModel.CurrentSong = first;
        viewModel.IsShuffleEnabled = true;

        await viewModel.NextTrack();
        Assert.Same(second, viewModel.CurrentSong);

        await viewModel.NextTrack();
        Assert.Same(first, viewModel.CurrentSong);
    }

    [Fact]
    public void SeekSlider_TracksPositionAndDisablesDuringSeekTransitions()
    {
        var repeat = new RepeatViewModel();
        var audio = new FakeAudioService();
        using PlaybackViewModel playback = CreateViewModel(audio, repeat);
        playback.CurrentSong = CreateYouTubeSong("Seekable", "jNQXAC9IVRw");
        using var slider = new SeekSliderViewModel(
            audio,
            playback,
            new FakeNotificationService());

        audio.SetDuration(TimeSpan.FromMinutes(2));
        audio.SetStatus(PlaybackStatus.Playing);
        audio.SetPosition(TimeSpan.FromSeconds(42));

        Assert.Equal(120, slider.MaximumSeekValue);
        Assert.Equal(42, slider.SeekPosition);
        Assert.True(slider.CanInteractSeekSlider);

        audio.SetStatus(PlaybackStatus.Seeking);
        Assert.False(slider.CanInteractSeekSlider);

        audio.SetStatus(PlaybackStatus.Playing);
        Assert.True(slider.CanInteractSeekSlider);
    }

    [Fact]
    public async Task PlayNext_InsertsImmediatelyAfterCurrentSong()
    {
        var repeat = new RepeatViewModel { RepeatMode = RepeatMode.Off };
        var audio = new FakeAudioService();
        using PlaybackViewModel viewModel = CreateViewModel(audio, repeat);

        Song current = CreateYouTubeSong("Current", "jNQXAC9IVRw");
        Song queued = CreateYouTubeSong("Already queued", "aaaaaaaaaaa");
        Song playNext = CreateYouTubeSong("Play next", "dQw4w9WgXcQ");

        viewModel.CurrentPlaylist = CreatePlaylist(current, queued);
        viewModel.CurrentSong = current;

        await viewModel.PlayNext(playNext);

        Assert.Equal(3, viewModel.CurrentPlaylist!.Songs.Count);
        Assert.Equal("Current", viewModel.CurrentPlaylist.Songs[0].Title);
        Assert.Equal("Play next", viewModel.CurrentPlaylist.Songs[1].Title);
        Assert.Equal("Already queued", viewModel.CurrentPlaylist.Songs[2].Title);
        Assert.Same(current, viewModel.CurrentSong);
        Assert.Empty(audio.StartedSources);
    }

    [Fact]
    public async Task PlayNext_CreatesQueueWhenEmpty()
    {
        var repeat = new RepeatViewModel();
        var audio = new FakeAudioService();
        using PlaybackViewModel viewModel = CreateViewModel(audio, repeat);
        Song song = CreateYouTubeSong("Solo", "jNQXAC9IVRw");

        await viewModel.PlayNext(song);

        Assert.Equal("Up Next", viewModel.CurrentPlaylist?.Name);
        Song queued = Assert.Single(viewModel.CurrentPlaylist!.Songs);
        Assert.Equal(song.VideoId, queued.VideoId);
        Assert.NotSame(song, queued);
        Assert.Empty(audio.StartedSources);
    }

    [Fact]
    public async Task PlayNext_AllowsDuplicateSongsInQueue()
    {
        var repeat = new RepeatViewModel();
        var audio = new FakeAudioService();
        using PlaybackViewModel viewModel = CreateViewModel(audio, repeat);

        Song current = CreateYouTubeSong("Current", "jNQXAC9IVRw");
        Song favorite = CreateYouTubeSong("Favorite", "dQw4w9WgXcQ");
        viewModel.CurrentPlaylist = CreatePlaylist(current);
        viewModel.CurrentSong = current;

        await viewModel.PlayNext(favorite);
        await viewModel.PlayNext(favorite);

        Assert.Equal(3, viewModel.CurrentPlaylist!.Songs.Count);
        Assert.Equal("Current", viewModel.CurrentPlaylist.Songs[0].Title);
        Assert.Equal("Favorite", viewModel.CurrentPlaylist.Songs[1].Title);
        Assert.Equal("Favorite", viewModel.CurrentPlaylist.Songs[2].Title);
        Assert.NotSame(
            viewModel.CurrentPlaylist.Songs[1],
            viewModel.CurrentPlaylist.Songs[2]);
    }

    [Fact]
    public void MoveUpNext_ReordersUpcomingSongsOnly()
    {
        var repeat = new RepeatViewModel();
        var audio = new FakeAudioService();
        using PlaybackViewModel viewModel = CreateViewModel(audio, repeat);

        Song current = CreateYouTubeSong("Current", "jNQXAC9IVRw");
        Song first = CreateYouTubeSong("First", "aaaaaaaaaaa");
        Song second = CreateYouTubeSong("Second", "bbbbbbbbbbb");
        viewModel.CurrentPlaylist = CreatePlaylist(current, first, second);
        viewModel.CurrentSong = current;

        Assert.True(viewModel.MoveUpNext(0, 1));

        Assert.Equal("Current", viewModel.CurrentPlaylist!.Songs[0].Title);
        Assert.Equal("Second", viewModel.CurrentPlaylist.Songs[1].Title);
        Assert.Equal("First", viewModel.CurrentPlaylist.Songs[2].Title);

        IReadOnlyList<Song> upcoming = viewModel.GetUpcomingSongs();
        Assert.Equal(2, upcoming.Count);
        Assert.Equal("Second", upcoming[0].Title);
        Assert.Equal("First", upcoming[1].Title);
    }

    [Fact]
    public async Task AddToUpNext_AppendsToEndOfQueue()
    {
        var repeat = new RepeatViewModel();
        var audio = new FakeAudioService();
        using PlaybackViewModel viewModel = CreateViewModel(audio, repeat);

        Song current = CreateYouTubeSong("Current", "jNQXAC9IVRw");
        Song first = CreateYouTubeSong("First", "aaaaaaaaaaa");
        Song second = CreateYouTubeSong("Second", "bbbbbbbbbbb");
        viewModel.CurrentPlaylist = CreatePlaylist(current, first);
        viewModel.CurrentSong = current;

        await viewModel.AddToUpNext(second);

        Assert.Equal(3, viewModel.CurrentPlaylist!.Songs.Count);
        Assert.Equal("Current", viewModel.CurrentPlaylist.Songs[0].Title);
        Assert.Equal("First", viewModel.CurrentPlaylist.Songs[1].Title);
        Assert.Equal("Second", viewModel.CurrentPlaylist.Songs[2].Title);
    }

    [Fact]
    public void RemoveFromUpNextAt_RemovesByUpcomingIndex()
    {
        var repeat = new RepeatViewModel();
        var audio = new FakeAudioService();
        using PlaybackViewModel viewModel = CreateViewModel(audio, repeat);

        Song current = CreateYouTubeSong("Current", "jNQXAC9IVRw");
        Song first = CreateYouTubeSong("First", "aaaaaaaaaaa");
        Song second = CreateYouTubeSong("Second", "bbbbbbbbbbb");
        viewModel.CurrentPlaylist = CreatePlaylist(current, first, second);
        viewModel.CurrentSong = current;

        Assert.True(viewModel.RemoveFromUpNextAt(0));
        Assert.Equal("Second", Assert.Single(viewModel.GetUpcomingSongs()).Title);
    }

    [Fact]
    public void CurrentPlaylist_AssignmentSnapshotsSourcePlaylist()
    {
        var repeat = new RepeatViewModel();
        var audio = new FakeAudioService();
        using PlaybackViewModel viewModel = CreateViewModel(audio, repeat);

        Song current = CreateYouTubeSong("Current", "jNQXAC9IVRw");
        Song first = CreateYouTubeSong("First", "aaaaaaaaaaa");
        Playlist source = CreatePlaylist(current, first);
        viewModel.CurrentPlaylist = source;
        viewModel.CurrentSong = current;

        Assert.NotSame(source, viewModel.CurrentPlaylist);
        Assert.NotSame(source.Songs, viewModel.CurrentPlaylist!.Songs);

        // Queue edits must never touch the stored playlist's collection.
        Assert.True(viewModel.RemoveFromUpNext(first));
        Assert.Empty(viewModel.GetUpcomingSongs());
        Assert.Equal(2, source.Songs.Count);
        Assert.Same(first, source.Songs[1]);
    }

    [Fact]
    public async Task PlayFromBeginning_DetachedSongLeavesUpNextUntouched()
    {
        var repeat = new RepeatViewModel();
        var audio = new FakeAudioService();
        using PlaybackViewModel viewModel = CreateViewModel(audio, repeat);

        Song current = CreateYouTubeSong("Current", "jNQXAC9IVRw");
        Song first = CreateYouTubeSong("First", "aaaaaaaaaaa");
        Song second = CreateYouTubeSong("Second", "bbbbbbbbbbb");
        viewModel.CurrentPlaylist = CreatePlaylist(current, first, second);
        viewModel.CurrentSong = current;

        // e.g. a history entry: a clone that is not part of the queue.
        Song detached = CreateYouTubeSong("Detached", "ccccccccccc");
        await viewModel.PlayFromBeginning(detached);

        Assert.Same(detached, viewModel.CurrentSong);
        IReadOnlyList<Song> upcoming = viewModel.GetUpcomingSongs();
        Assert.Equal(2, upcoming.Count);
        Assert.Equal("First", upcoming[0].Title);
        Assert.Equal("Second", upcoming[1].Title);

        // The detached track must never be added to the queue itself.
        Assert.Equal(3, viewModel.CurrentPlaylist!.Songs.Count);
        Assert.DoesNotContain(detached, viewModel.CurrentPlaylist.Songs);
    }

    [Fact]
    public async Task DetachedSong_TrackEnd_ResumesQueueWhereItLeftOff()
    {
        var repeat = new RepeatViewModel { RepeatMode = RepeatMode.Off };
        var audio = new FakeAudioService();
        using PlaybackViewModel viewModel = CreateViewModel(audio, repeat);

        Song current = CreateYouTubeSong("Current", "jNQXAC9IVRw");
        Song first = CreateYouTubeSong("First", "aaaaaaaaaaa");
        viewModel.CurrentPlaylist = CreatePlaylist(current, first);
        viewModel.CurrentSong = current;

        Song detached = CreateYouTubeSong("Detached", "ccccccccccc");
        await viewModel.PlayFromBeginning(detached);
        Assert.Single(audio.StartedSources);

        audio.RaiseTrackEnded();
        await WaitUntilAsync(() => audio.StartedSources.Count == 2);

        Assert.Same(first, viewModel.CurrentSong);
    }

    [Fact]
    public void RemoveFromUpNext_RemovesQueuedSongOnly()
    {
        var repeat = new RepeatViewModel();
        var audio = new FakeAudioService();
        using PlaybackViewModel viewModel = CreateViewModel(audio, repeat);

        Song current = CreateYouTubeSong("Current", "jNQXAC9IVRw");
        Song first = CreateYouTubeSong("First", "aaaaaaaaaaa");
        Song second = CreateYouTubeSong("Second", "bbbbbbbbbbb");
        viewModel.CurrentPlaylist = CreatePlaylist(current, first, second);
        viewModel.CurrentSong = current;

        Assert.True(viewModel.RemoveFromUpNext(first));
        Assert.False(viewModel.RemoveFromUpNext(current));

        Assert.Equal(2, viewModel.CurrentPlaylist!.Songs.Count);
        Assert.Equal("Current", viewModel.CurrentPlaylist.Songs[0].Title);
        Assert.Equal("Second", viewModel.CurrentPlaylist.Songs[1].Title);
        Assert.Equal("Second", Assert.Single(viewModel.GetUpcomingSongs()).Title);
    }

    public void Dispose()
    {
        _themes.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private PlaybackViewModel CreateViewModel(
        FakeAudioService audio,
        RepeatViewModel repeat)
    {
        return new PlaybackViewModel(
            audio,
            repeat,
            new FakeLastFmDataService(),
            _themes,
            new FakeNotificationService());
    }

    private static Playlist CreatePlaylist(params Song[] songs)
    {
        return new Playlist
        {
            Name = "Queue",
            Songs = new ObservableCollection<Song>(songs)
        };
    }

    private static Song CreateYouTubeSong(string title, string videoId)
    {
        return new Song
        {
            Title = title,
            Artist = "Test artist",
            VideoId = videoId
        };
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class FakeAudioService : IAudioService
    {
        private PlaybackStatus _status = PlaybackStatus.Stopped;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? TrackEnded;
        public event EventHandler? PlaybackStateChanged;
        public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed
        {
            add { }
            remove { }
        }

        public List<PlaybackSource> StartedSources { get; } = [];
        public PlaybackStatus Status => _status;
        public bool IsPlaying => Status == PlaybackStatus.Playing;
        public bool IsPaused => Status == PlaybackStatus.Paused;
        public bool IsStopped => Status is PlaybackStatus.Stopped or PlaybackStatus.Failed;
        public bool IsSeekBuffering => Status is PlaybackStatus.Loading or PlaybackStatus.Seeking;
        public TimeSpan CurrentPosition { get; private set; }
        public TimeSpan TotalDuration { get; private set; } = TimeSpan.FromMinutes(3);
        public float AudioVolume { get; set; }

        public TaskCompletionSource<bool>? StartGate { get; set; }

        public async Task StartAsync(
            PlaybackSource source,
            TimeSpan startingPosition = default,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartedSources.Add(source);
            CurrentPosition = startingPosition;
            SetStatus(PlaybackStatus.Loading);
            if (StartGate is not null)
            {
                await StartGate.Task.WaitAsync(cancellationToken);
            }

            SetStatus(PlaybackStatus.Playing);
        }

        public Task SeekAsync(
            TimeSpan position,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CurrentPosition = position;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(CurrentPosition)));
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetStatus(PlaybackStatus.Paused);
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetStatus(PlaybackStatus.Playing);
            return Task.CompletedTask;
        }

        public Task RestartAsync(CancellationToken cancellationToken = default)
        {
            return SeekAsync(TimeSpan.Zero, cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetStatus(PlaybackStatus.Stopped);
            return Task.CompletedTask;
        }

        public void RaiseTrackEnded() => TrackEnded?.Invoke(this, EventArgs.Empty);

        public void SetDuration(TimeSpan duration)
        {
            TotalDuration = duration;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(TotalDuration)));
        }

        public void SetPosition(TimeSpan position)
        {
            CurrentPosition = position;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(CurrentPosition)));
        }

        public void SetStatus(PlaybackStatus status)
        {
            _status = status;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(IsSeekBuffering)));
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeNotificationService : IUserNotificationService
    {
        public void ShowError(string message)
        {
        }

        public void ShowInfo(string message)
        {
        }

        public void Clear()
        {
        }
    }

    private sealed class FakeLastFmDataService : ILastFmDataService
    {
        public event EventHandler? AuthenticationStateChanged
        {
            add { }
            remove { }
        }

        public bool IsConfigured => false;
        public bool IsAuthenticated => false;
        public string? LastError => null;
        public string Username => string.Empty;

        public Task<IEnumerable<Song>> GetTopTracksAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Enumerable.Empty<Song>());

        public Task<IEnumerable<Song>> GetRecentlyPlayedTracksAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Enumerable.Empty<Song>());

        public Task<IEnumerable<Song>> GetRecommendedAlbumsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Enumerable.Empty<Song>());

        public Task ScrobbleTrackAsync(string title, string artist, string album) =>
            Task.CompletedTask;

        public Task ScrobbleTrackAsync(
            string title,
            string artist,
            string album,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> UserExistsAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<LastFmWebAuth> StartWebAuthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LastFmWebAuth("token", "https://www.last.fm/api/auth/"));

        public Task<bool> WaitForWebAuthAsync(
            LastFmWebAuth auth,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public void SignOut()
        {
        }
    }
}
