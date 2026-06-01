using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Adam.ServiceManager.Controls;

/// <summary>
/// A simple animated loading spinner control.
/// </summary>
public class LoadingSpinner : Control
{
    static LoadingSpinner()
    {
        AffectsRender<LoadingSpinner>(IsSpinningProperty, ForegroundProperty);
    }

    public static readonly StyledProperty<bool> IsSpinningProperty =
        AvaloniaProperty.Register<LoadingSpinner, bool>(nameof(IsSpinning), true);

    public static readonly StyledProperty<double> SpinnerSizeProperty =
        AvaloniaProperty.Register<LoadingSpinner, double>(nameof(SpinnerSize), 40);

    /// <summary>
    /// Defines the <see cref="Foreground"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<LoadingSpinner, IBrush?>("Foreground", Brushes.Gray);

    public bool IsSpinning
    {
        get => GetValue(IsSpinningProperty);
        set => SetValue(IsSpinningProperty, value);
    }

    public double SpinnerSize
    {
        get => GetValue(SpinnerSizeProperty);
        set => SetValue(SpinnerSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used to draw the spinner arc.
    /// </summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = size / 2 - 2;
        var pen = new Pen(Foreground ?? Brushes.Gray, 3, lineCap: PenLineCap.Round);
        if (pen.Brush == null) return;

        // Draw a partial arc as a spinner
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var startAngle = -Math.PI / 2;
            var endAngle = startAngle + Math.PI * 1.5;
            var steps = 20;
            var angleStep = (endAngle - startAngle) / steps;

            var firstPoint = new Point(
                center.X + radius * Math.Cos(startAngle),
                center.Y + radius * Math.Sin(startAngle));
            ctx.BeginFigure(firstPoint, true);

            for (int i = 1; i <= steps; i++)
            {
                var angle = startAngle + angleStep * i;
                var point = new Point(
                    center.X + radius * Math.Cos(angle),
                    center.Y + radius * Math.Sin(angle));
                ctx.LineTo(point);
            }

            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }
}
