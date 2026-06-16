using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Adam.CatalogBrowser.Controls;

/// <summary>
/// Event args for zoom changes (used by compare view sync).
/// </summary>
public sealed class ZoomChangedEventArgs : EventArgs
{
    public double ZoomLevel { get; init; }
}

/// <summary>
/// Event args for pan changes (used by compare view sync).
/// </summary>
public sealed class PanChangedEventArgs : EventArgs
{
    public Vector PanOffset { get; init; }
}

/// <summary>
/// A custom control that wraps content with interactive pan and zoom.
/// Supports mouse-wheel zoom (centered on cursor), click-drag pan,
/// double-click to fit, and programmatic zoom/pan via properties.
///
/// Applies RenderTransform to itself and wires pointer events on itself.
/// </summary>
public sealed class ZoomBorder : ContentControl
{
    private TranslateTransform? _translate;
    private ScaleTransform? _scale;
    private bool _isPanning;
    private Point _panStart;
    private Vector _panStartOffset;

    /// <summary>
    /// Defines the <see cref="ZoomLevel"/> property.
    /// </summary>
    public static readonly DirectProperty<ZoomBorder, double> ZoomLevelProperty =
        AvaloniaProperty.RegisterDirect<ZoomBorder, double>(
            nameof(ZoomLevel),
            o => o.ZoomLevel,
            (o, v) => o.ZoomLevel = v,
            1.0);

    /// <summary>
    /// Defines the <see cref="PanOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<Vector> PanOffsetProperty =
        AvaloniaProperty.Register<ZoomBorder, Vector>(
            nameof(PanOffset),
            new Vector(0, 0));

    /// <summary>
    /// Defines the <see cref="MinZoom"/> property.
    /// </summary>
    public static readonly StyledProperty<double> MinZoomProperty =
        AvaloniaProperty.Register<ZoomBorder, double>(nameof(MinZoom), 0.1);

    /// <summary>
    /// Defines the <see cref="MaxZoom"/> property.
    /// </summary>
    public static readonly StyledProperty<double> MaxZoomProperty =
        AvaloniaProperty.Register<ZoomBorder, double>(nameof(MaxZoom), 20.0);

    /// <summary>
    /// Defines the <see cref="IsPanEnabled"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsPanEnabledProperty =
        AvaloniaProperty.Register<ZoomBorder, bool>(nameof(IsPanEnabled), true);

    /// <summary>
    /// Defines the <see cref="IsZoomEnabled"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsZoomEnabledProperty =
        AvaloniaProperty.Register<ZoomBorder, bool>(nameof(IsZoomEnabled), true);

    /// <summary>
    /// Current zoom level (1.0 = 100%, range 0.1-20.0).
    /// </summary>
    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            if (Math.Abs(_zoomLevel - value) < 0.001) return;
            value = Math.Clamp(value, MinZoom, MaxZoom);
            SetAndRaise(ZoomLevelProperty, ref _zoomLevel, value);
            UpdateTransform();
            ZoomChanged?.Invoke(this, new ZoomChangedEventArgs { ZoomLevel = value });
        }
    }
    private double _zoomLevel = 1.0;

    /// <summary>
    /// Pan offset in pixels.
    /// </summary>
    public Vector PanOffset
    {
        get => GetValue(PanOffsetProperty);
        set
        {
            SetValue(PanOffsetProperty, value);
            if (_translate != null)
            {
                _translate.X = value.X;
                _translate.Y = value.Y;
            }
            PanChanged?.Invoke(this, new PanChangedEventArgs { PanOffset = value });
        }
    }

    /// <summary>
    /// Minimum zoom level (default 0.1).
    /// </summary>
    public double MinZoom
    {
        get => GetValue(MinZoomProperty);
        set => SetValue(MinZoomProperty, value);
    }

    /// <summary>
    /// Maximum zoom level (default 20.0).
    /// </summary>
    public double MaxZoom
    {
        get => GetValue(MaxZoomProperty);
        set => SetValue(MaxZoomProperty, value);
    }

    /// <summary>
    /// Whether click-drag panning is enabled (default true).
    /// </summary>
    public bool IsPanEnabled
    {
        get => GetValue(IsPanEnabledProperty);
        set => SetValue(IsPanEnabledProperty, value);
    }

    /// <summary>
    /// Whether mouse-wheel zoom is enabled (default true).
    /// </summary>
    public bool IsZoomEnabled
    {
        get => GetValue(IsZoomEnabledProperty);
        set => SetValue(IsZoomEnabledProperty, value);
    }

    /// <summary>
    /// Fires when the zoom level changes (for compare view sync).
    /// </summary>
    public event EventHandler<ZoomChangedEventArgs>? ZoomChanged;

    /// <summary>
    /// Fires when the pan offset changes (for compare view sync).
    /// </summary>
    public event EventHandler<PanChangedEventArgs>? PanChanged;

    public ZoomBorder()
    {
        ClipToBounds = true;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        // Set up the render transform on this control
        _scale = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
        _translate = new TranslateTransform();
        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(_scale);
        transformGroup.Children.Add(_translate);
        RenderTransform = transformGroup;
        RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);

        // Wire pointer events
        PointerWheelChanged += OnPointerWheelChanged;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        Tapped += OnDoubleTapped;
    }

    /// <summary>
    /// Fits the content to the available viewport (zoom to 1:1).
    /// </summary>
    public void FitToWindow()
    {
        ZoomLevel = 1.0;
        PanOffset = new Vector(0, 0);
    }

    /// <summary>
    /// Zooms to a specific factor around a center point (in control coordinates).
    /// </summary>
    public void ZoomTo(double factor, Point? center = null)
    {
        var previousZoom = ZoomLevel;
        ZoomLevel = factor;

        if (center.HasValue && _translate != null && _scale != null)
        {
            var c = center.Value;
            var ratio = ZoomLevel / previousZoom;
            UpdateTransform();
            var newX = c.X - ratio * (c.X - _translate.X);
            var newY = c.Y - ratio * (c.Y - _translate.Y);
            PanOffset = new Vector(newX, newY);
        }
    }

    /// <summary>
    /// Pans by a relative delta (in pixels).
    /// </summary>
    public void PanBy(Vector delta)
    {
        PanOffset = new Vector(PanOffset.X + delta.X, PanOffset.Y + delta.Y);
    }

    public void BeginBatchUpdate() { }
    public void EndBatchUpdate() { }

    private void UpdateTransform()
    {
        if (_scale == null) return;
        _scale.ScaleX = _zoomLevel;
        _scale.ScaleY = _zoomLevel;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!IsZoomEnabled) return;

        var delta = e.Delta.Y;
        if (Math.Abs(delta) < 0.01) return;

        var zoomFactor = delta > 0 ? 1.15 : 1.0 / 1.15;
        var newZoom = Math.Clamp(ZoomLevel * zoomFactor, MinZoom, MaxZoom);

        var mousePos = e.GetPosition(this);
        var previousZoom = ZoomLevel;
        ZoomLevel = newZoom;

        if (_translate != null)
        {
            var ratio = ZoomLevel / previousZoom;
            UpdateTransform();
            var newX = mousePos.X - ratio * (mousePos.X - _translate.X);
            var newY = mousePos.Y - ratio * (mousePos.Y - _translate.Y);
            PanOffset = new Vector(newX, newY);
        }

        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsPanEnabled) return;

        _isPanning = true;
        _panStart = e.GetPosition(this);
        _panStartOffset = PanOffset;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPanning || !IsPanEnabled) return;

        var currentPos = e.GetPosition(this);
        var delta = currentPos - _panStart;
        PanOffset = new Vector(
            _panStartOffset.X + delta.X,
            _panStartOffset.Y + delta.Y);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isPanning = false;
        Cursor = new Cursor(StandardCursorType.Arrow);
        e.Pointer.Capture(null);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        FitToWindow();
        e.Handled = true;
    }
}
