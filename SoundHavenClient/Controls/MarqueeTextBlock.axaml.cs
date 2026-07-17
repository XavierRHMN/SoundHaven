using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace SoundHaven.Controls;

/// <summary>
/// A single-line text block that ping-pong scrolls its text when it is too wide
/// to fit, and stays static otherwise. Used for list rows whose titles would
/// otherwise be clipped to an ellipsis.
/// </summary>
public partial class MarqueeTextBlock : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, string?>(nameof(Text));

    private const double Step = 1.0;
    private const int PauseTicks = 45; // ~0.9s at 20ms per tick

    private readonly TranslateTransform _shift = new();
    private DispatcherTimer? _timer;
    private double _overflow;
    private double _position;
    private int _direction = -1;
    private int _pause = PauseTicks;

    public MarqueeTextBlock()
    {
        InitializeComponent();
        Label.RenderTransform = _shift;
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            Label.Text = Text ?? string.Empty;
            // Re-measure once the new text has laid out.
            Dispatcher.UIThread.Post(EvaluateOverflow, DispatcherPriority.Loaded);
        }
        else if (change.Property == BoundsProperty)
        {
            EvaluateOverflow();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopTimer();
    }

    private void EvaluateOverflow()
    {
        double viewport = Viewport.Bounds.Width;
        Label.Measure(Size.Infinity);
        double textWidth = Label.DesiredSize.Width;

        _overflow = textWidth - viewport;
        if (_overflow > 1 && viewport > 0)
        {
            StartTimer();
        }
        else
        {
            StopTimer();
            _position = 0;
            _shift.X = 0;
        }
    }

    private void StartTimer()
    {
        if (_timer is not null)
        {
            return;
        }

        _position = 0;
        _direction = -1;
        _pause = PauseTicks;
        _shift.X = 0;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void StopTimer()
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Tick -= OnTick;
        _timer.Stop();
        _timer = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_pause > 0)
        {
            _pause--;
            return;
        }

        _position += _direction * Step;
        if (_position <= -_overflow)
        {
            _position = -_overflow;
            _direction = 1;
            _pause = PauseTicks;
        }
        else if (_position >= 0)
        {
            _position = 0;
            _direction = -1;
            _pause = PauseTicks;
        }

        _shift.X = _position;
    }
}
