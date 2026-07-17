using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using SoundHaven.Models;
using SoundHaven.Services;

namespace SoundHaven.ViewModels;

public sealed class SongInfoViewModel : ViewModelBase
{
    private readonly PlaybackViewModel _playbackViewModel;
    private readonly IAudioService _audioService;
    private readonly IAlbumArtService _albumArtService;
    private readonly DispatcherTimer _scrollTimer;
    private readonly DispatcherTimer _pauseTimer;
    private bool _isScrollingNeeded;
    private bool _isPaused;
    private bool _hasPausedThisCycle;

    private Song? _currentSong;
    public Song? CurrentSong
    {
        get => _currentSong;
        private set
        {
            Song? previous = _currentSong;
            if (SetProperty(ref _currentSong, value))
            {
                if (previous is not null)
                {
                    previous.PropertyChanged -= OnCurrentSongPropertyChanged;
                }

                if (_currentSong is not null)
                {
                    _currentSong.PropertyChanged += OnCurrentSongPropertyChanged;
                }

                OnPropertyChanged(nameof(CurrentSongExists));
                OnPropertyChanged(nameof(ArtistAndYearText));
                _hasPausedThisCycle = false;
                TextWidth = ExtractTextWidth(CurrentSong?.Title, "Circular", TitleFontSize);
                UpdateScrollingState();
                TryResolveYear(_currentSong);
            }
        }
    }

    public bool CurrentSongExists => CurrentSong is not null;

    /// <summary>"Artist - Year", degrading to just the artist while the year is unknown.</summary>
    public string ArtistAndYearText
    {
        get
        {
            if (CurrentSong is null)
            {
                return string.Empty;
            }

            string artist = CurrentSong.Artist?.Trim() ?? string.Empty;
            if (CurrentSong.Year is not int year || year <= 0)
            {
                return artist;
            }

            return artist.Length > 0
                ? FormattableString.Invariant($"{artist} - {year}")
                : year.ToString(CultureInfo.InvariantCulture);
        }
    }

    private bool _isSeekBuffering;
    public bool IsSeekBuffering
    {
        get => _isSeekBuffering;
        set => SetProperty(ref _isSeekBuffering, value);
    }

    private double _titleScrollPosition1;
    public double TitleScrollPosition1
    {
        get => _titleScrollPosition1;
        set => SetProperty(ref _titleScrollPosition1, value);
    }

    private double _titleScrollPosition2;
    public double TitleScrollPosition2
    {
        get => _titleScrollPosition2;
        set => SetProperty(ref _titleScrollPosition2, value);
    }

    private double _textWidth;
    public double TextWidth
    {
        get => _textWidth;
        set
        {
            SetProperty(ref _textWidth, value);
            OnPropertyChanged(nameof(TitleWidth));
            UpdateScrollingState();
        }
    }

    /// <summary>
    /// The title font size used both for on-screen rendering and width
    /// measurement, so the reserved title area matches the glyphs exactly.
    /// </summary>
    private const double TitleFontSize = 16;

    /// <summary>A few pixels of slack so the final glyph never clips.</summary>
    private const double TitlePadding = 6;

    /// <summary>
    /// Width actually reserved for the title: it hugs short titles (so the
    /// heart/menu sit right beside them) and caps at <see cref="ControlWidth"/>
    /// for long titles, which then scroll within that fixed viewport.
    /// </summary>
    public double TitleWidth => Math.Min(TextWidth + TitlePadding, ControlWidth);

    public double ControlWidth { get; set; } = 220; // Max width of the title before it scrolls

    public SongInfoViewModel(
        PlaybackViewModel playbackViewModel,
        IAudioService audioService,
        IAlbumArtService albumArtService)
    {
        _playbackViewModel = playbackViewModel ?? throw new ArgumentNullException(nameof(playbackViewModel));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _albumArtService = albumArtService ?? throw new ArgumentNullException(nameof(albumArtService));
        _pauseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _pauseTimer.Tick += (_, _) =>
        {
            _isPaused = false;
            _pauseTimer.Stop();
        };
        _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _scrollTimer.Tick += (_, _) => ScrollText();
        _playbackViewModel.PropertyChanged += PlaybackViewModel_PropertyChanged;
        _audioService.PropertyChanged += AudioService_PropertyChanged;

        CurrentSong = _playbackViewModel.CurrentSong;
        UpdateBufferingState();
    }

    private void PlaybackViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaybackViewModel.CurrentSong))
        {
            CurrentSong = _playbackViewModel.CurrentSong;
            UpdateBufferingState();
        }
        else if (e.PropertyName == nameof(PlaybackViewModel.IsTransitioningTracks))
        {
            UpdateBufferingState();
        }
    }

    private void AudioService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IAudioService.IsSeekBuffering) or nameof(IAudioService.Status))
        {
            UpdateBufferingState();
        }
    }

    private void UpdateBufferingState()
    {
        IsSeekBuffering = _playbackViewModel.IsTransitioningTracks
            || (_audioService.IsSeekBuffering && CurrentSong?.VideoId is not null);
    }

    private void OnCurrentSongPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Song.Artist) or nameof(Song.Year))
        {
            OnPropertyChanged(nameof(ArtistAndYearText));
        }
    }

    /// <summary>
    /// YouTube tracks carry no release year, so resolve it online (local files
    /// already get theirs from tags).
    /// </summary>
    private void TryResolveYear(Song? song)
    {
        if (song is null
            || song.Year is not null
            || string.IsNullOrWhiteSpace(song.VideoId)
            || string.IsNullOrWhiteSpace(song.Title))
        {
            return;
        }

        _ = ResolveYearAsync(song);
    }

    private async Task ResolveYearAsync(Song song)
    {
        try
        {
            int? year = await _albumArtService
                .GetTrackYearAsync(song.Artist, song.Title)
                .ConfigureAwait(false);
            if (year is null)
            {
                return;
            }

            // Avalonia doesn't marshal INPC off-thread; assign on the UI thread.
            await Dispatcher.UIThread.InvokeAsync(() => song.Year ??= year);
        }
        catch
        {
            // The year is decorative; lookups must never disturb the player bar.
        }
    }

    private void UpdateScrollingState()
    {
        // Only scroll when the title genuinely overflows the fixed viewport;
        // shorter titles hug their text (see TitleWidth) and stay static.
        _isScrollingNeeded = TextWidth > ControlWidth;

        if (_isScrollingNeeded)
        {
            InitializeScrollPositions();
            if (!_scrollTimer.IsEnabled)
            {
                _scrollTimer.Start();
            }
        }
        else
        {
            _scrollTimer.Stop();
            ResetScrollPositions();
        }
    }

    private void InitializeScrollPositions()
    {
        const double spacing = 50; // Adjust spacing as needed
        const double initialAdjustment = 50;

        TitleScrollPosition1 = initialAdjustment;
        TitleScrollPosition2 = TextWidth + initialAdjustment + spacing;
    }

    private void ResetScrollPositions()
    {
        TitleScrollPosition1 = 0;
        TitleScrollPosition2 = ControlWidth; // Move the second instance out of view
    }

    private void ScrollText()
    {
        if (!_isScrollingNeeded || _isPaused) return;

        const double scrollSpeed = 2; // Adjust scroll speed as needed
        const double spacing = 50; // Spacing between the two TextBlocks

        // Update both scroll positions
        TitleScrollPosition1 -= scrollSpeed;
        TitleScrollPosition2 -= scrollSpeed;

        // Check if either position has reached 0 and we haven't paused in this cycle
        if (!_hasPausedThisCycle && (Math.Abs(TitleScrollPosition1) < scrollSpeed || Math.Abs(TitleScrollPosition2) < scrollSpeed))
        {
            _isPaused = true;
            _hasPausedThisCycle = true;
            _pauseTimer.Start();
        }

        // Reset the first TextBlock if it's completely out of view
        if (TitleScrollPosition1 <= -TextWidth)
        {
            TitleScrollPosition1 = TitleScrollPosition2 + TextWidth + spacing;
            _hasPausedThisCycle = false;
        }

        // Reset the second TextBlock if it's completely out of view
        if (TitleScrollPosition2 <= -TextWidth)
        {
            TitleScrollPosition2 = TitleScrollPosition1 + TextWidth + spacing;
            _hasPausedThisCycle = false;
        }
    }

    private static double ExtractTextWidth(string? text, string fontFamily, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var typeface = new Typeface(fontFamily);
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black
        );

        return formattedText.Width;
    }

    public override void Dispose()
    {
        _playbackViewModel.PropertyChanged -= PlaybackViewModel_PropertyChanged;
        _audioService.PropertyChanged -= AudioService_PropertyChanged;
        if (_currentSong is not null)
        {
            _currentSong.PropertyChanged -= OnCurrentSongPropertyChanged;
        }

        _pauseTimer.Stop();
        _scrollTimer.Stop();
        base.Dispose();
    }
}
