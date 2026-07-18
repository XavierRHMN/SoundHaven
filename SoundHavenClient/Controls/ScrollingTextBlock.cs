using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace SoundHaven.Controls;

/// <summary>
/// A single-line text display that continuously marquee-scrolls whenever the
/// text is wider than the control, and stays static otherwise. Font settings
/// are taken from the control's own inherited Font*/Foreground properties.
/// </summary>
public partial class ScrollingTextBlock : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ScrollingTextBlock, string?>(nameof(Text));

    private const double ScrollSpeed = 2;
    private const double LoopSpacing = 50;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan CyclePause = TimeSpan.FromSeconds(3);

    private readonly DispatcherTimer _scrollTimer;
    private readonly DispatcherTimer _pauseTimer;
    private double _textWidth;
    private double _position1;
    private double _position2;
    private bool _isScrolling;
    private bool _isPaused;
    private bool _hasPausedThisCycle;

    public ScrollingTextBlock()
    {
        InitializeComponent();

        _pauseTimer = new DispatcherTimer { Interval = CyclePause };
        _pauseTimer.Tick += (_, _) =>
        {
            _isPaused = false;
            _pauseTimer.Stop();
        };
        _scrollTimer = new DispatcherTimer { Interval = TickInterval };
        _scrollTimer.Tick += (_, _) => ScrollTick();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty
            || change.Property == FontSizeProperty
            || change.Property == FontFamilyProperty
            || change.Property == FontWeightProperty)
        {
            Refresh();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Refresh();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _scrollTimer.Stop();
        _pauseTimer.Stop();
    }

    private void Refresh()
    {
        string text = Text ?? string.Empty;
        PrimaryText.Text = text;
        SecondaryText.Text = text;

        if (text.Length == 0)
        {
            StopScrolling();
            return;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight),
            FontSize,
            Brushes.White);
        _textWidth = formatted.Width;
        MinHeight = Math.Ceiling(formatted.Height);

        double viewport = Bounds.Width;
        bool shouldScroll = VisualRoot is not null
            && viewport > 0
            && _textWidth > viewport + 1;

        if (!shouldScroll)
        {
            StopScrolling();
            return;
        }

        if (_isScrolling && _scrollTimer.IsEnabled)
        {
            return;
        }

        // Start readable (a full pause at the origin), then loop continuously.
        _isScrolling = true;
        _position1 = 0;
        _position2 = _textWidth + LoopSpacing;
        _isPaused = true;
        _hasPausedThisCycle = true;
        ApplyPositions();
        SecondaryText.IsVisible = true;
        _pauseTimer.Stop();
        _pauseTimer.Start();
        _scrollTimer.Start();
    }

    private void StopScrolling()
    {
        _scrollTimer.Stop();
        _pauseTimer.Stop();
        _isScrolling = false;
        _isPaused = false;
        _hasPausedThisCycle = false;
        _position1 = 0;
        _position2 = 0;
        SecondaryText.IsVisible = false;
        ApplyPositions();
    }

    private void ScrollTick()
    {
        if (!_isScrolling || _isPaused)
        {
            return;
        }

        _position1 -= ScrollSpeed;
        _position2 -= ScrollSpeed;

        // Pause once per loop as a copy lines up with the viewport start.
        if (!_hasPausedThisCycle
            && (Math.Abs(_position1) < ScrollSpeed || Math.Abs(_position2) < ScrollSpeed))
        {
            _isPaused = true;
            _hasPausedThisCycle = true;
            _pauseTimer.Start();
        }

        if (_position1 <= -_textWidth)
        {
            _position1 = _position2 + _textWidth + LoopSpacing;
            _hasPausedThisCycle = false;
        }

        if (_position2 <= -_textWidth)
        {
            _position2 = _position1 + _textWidth + LoopSpacing;
            _hasPausedThisCycle = false;
        }

        ApplyPositions();
    }

    private void ApplyPositions()
    {
        Canvas.SetLeft(PrimaryText, _position1);
        Canvas.SetLeft(SecondaryText, _position2);
    }
}
