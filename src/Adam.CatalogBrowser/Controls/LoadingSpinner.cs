using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;

namespace Adam.CatalogBrowser.Controls;

/// <summary>
/// A loading spinner control that displays three pulsing dots.
/// Set <see cref="Foreground"/> to change color, <see cref="DotSize"/> to scale.
/// </summary>
public class LoadingSpinner : TemplatedControl
{
    private readonly DispatcherTimer _timer;
    private long _startTick;
    private bool _isAttached;

    /// <summary>
    /// Whether the spinner should be actively animating.
    /// </summary>
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<LoadingSpinner, bool>(nameof(IsActive), true);

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>
    /// The size of each dot in pixels (default 8).
    /// </summary>
    public static readonly StyledProperty<double> DotSizeProperty =
        AvaloniaProperty.Register<LoadingSpinner, double>(nameof(DotSize), 8.0);

    public double DotSize
    {
        get => GetValue(DotSizeProperty);
        set => SetValue(DotSizeProperty, value);
    }

    public LoadingSpinner()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnTimerTick;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (IsActive && _isAttached)
            InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        _startTick = Environment.TickCount;
        if (IsActive)
            _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isAttached = false;
        _timer.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty)
        {
            if (IsActive && _isAttached)
                _timer.Start();
            else
                _timer.Stop();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var dotSize = DotSize;
        var spacing = dotSize * 1.8;
        var totalWidth = dotSize * 3 + spacing * 2;
        var height = dotSize;
        return new Size(totalWidth, height);
    }

    public override void Render(DrawingContext context)
    {
        if (!IsActive) return;

        if (Foreground is not SolidColorBrush scb) return;

        var dotSize = DotSize;
        var spacing = dotSize * 1.8;
        var totalWidth = dotSize * 3 + spacing * 2;
        var startX = (Bounds.Width - totalWidth) / 2.0;
        var centerY = Bounds.Height / 2.0;

        // Use unsigned arithmetic to correctly wrap across TickCount overflow (~25 days)
        var elapsed = ((uint)Environment.TickCount - (uint)_startTick) % 1200; // 1.2s cycle

        for (int i = 0; i < 3; i++)
        {
            var x = startX + i * (dotSize + spacing);
            var rect = new Rect(x, centerY - dotSize / 2, dotSize, dotSize);

            // Stagger: dot 1 at phase 0, dot 2 delayed by 400ms, dot 3 delayed by 800ms
            var dotPhase = (elapsed - (uint)(i * 400) + 1200) % 1200;
            // Normalize to 0..1, pulse from 0.25 to 1.0
            var t = dotPhase / 1200.0;
            var opacity = 0.25 + 0.75 * Math.Pow(Math.Sin(t * Math.PI * 2), 4);

            context.FillRectangle(new SolidColorBrush(scb.Color, opacity), rect, (float)(dotSize / 2));
        }
    }
}
