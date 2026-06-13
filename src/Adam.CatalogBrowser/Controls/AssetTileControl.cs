using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;

namespace Adam.CatalogBrowser.Controls;

/// <summary>
/// A custom templated control for displaying an asset tile in the gallery.
/// Designed for use as the content of a ListBox item template.
///
/// Layout (top to bottom):
///   - Toolbar row    (bound <see cref="ToolbarActions"/> collection)
///   - Thumbnail      (bound <see cref="Thumbnail"/> / <see cref="ThumbnailSize"/>)
///   - Metadata row   (<see cref="Rating"/>, <see cref="ColorLabel"/>, <see cref="IsFlagged"/>)
///   - Text field 1   (<see cref="TextField1Label"/> / <see cref="TextField1"/>)
///   - Text field 2   (<see cref="TextField2Label"/> / <see cref="TextField2"/>)
///   - Text field 3   (<see cref="TextField3Label"/> / <see cref="TextField3"/>)
/// </summary>
public class AssetTileControl : TemplatedControl
{
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // T11.10: Manage pseudo-class for search highlight style trigger
        if (change.Property == IsSearchHighlightedProperty)
        {
            PseudoClasses.Set(":isearchhighlighted", change.NewValue is true);
        }
    }

    // ──────────────────────────────────────────────
    //  Thumbnail
    // ──────────────────────────────────────────────

    /// <summary>
    /// Defines the <see cref="Thumbnail"/> property.
    /// </summary>
    public static readonly StyledProperty<IImage?> ThumbnailProperty =
        AvaloniaProperty.Register<AssetTileControl, IImage?>(nameof(Thumbnail));

    /// <summary>
    /// Defines the <see cref="ThumbnailSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ThumbnailSizeProperty =
        AvaloniaProperty.Register<AssetTileControl, double>(nameof(ThumbnailSize), 150.0);

    /// <summary>
    /// The thumbnail image to display.
    /// </summary>
    public IImage? Thumbnail
    {
        get => GetValue(ThumbnailProperty);
        set => SetValue(ThumbnailProperty, value);
    }

    /// <summary>
    /// Width and height of the thumbnail area in pixels.
    /// </summary>
    public double ThumbnailSize
    {
        get => GetValue(ThumbnailSizeProperty);
        set => SetValue(ThumbnailSizeProperty, value);
    }

    // ──────────────────────────────────────────────
    //  Rating
    // ──────────────────────────────────────────────

    /// <summary>
    /// Defines the <see cref="Rating"/> property.
    /// </summary>
    public static readonly StyledProperty<int> RatingProperty =
        AvaloniaProperty.Register<AssetTileControl, int>(nameof(Rating));

    /// <summary>
    /// Asset rating (typically 0-5).
    /// </summary>
    public int Rating
    {
        get => GetValue(RatingProperty);
        set => SetValue(RatingProperty, value);
    }

    // ──────────────────────────────────────────────
    //  Colour label
    // ──────────────────────────────────────────────

    /// <summary>
    /// Defines the <see cref="ColorLabel"/> property.
    /// </summary>
    public static readonly StyledProperty<string> ColorLabelProperty =
        AvaloniaProperty.Register<AssetTileControl, string>(nameof(ColorLabel), string.Empty);

    /// <summary>
    /// Colour label text (e.g. "Red", "Blue", "Green"). Rendered as a tag/swatch.
    /// </summary>
    public string ColorLabel
    {
        get => GetValue(ColorLabelProperty);
        set => SetValue(ColorLabelProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="ColorBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ColorBrushProperty =
        AvaloniaProperty.Register<AssetTileControl, IBrush?>(nameof(ColorBrush));

    /// <summary>
    /// Optional brush for rendering a colour swatch indicator alongside the label.
    /// </summary>
    public IBrush? ColorBrush
    {
        get => GetValue(ColorBrushProperty);
        set => SetValue(ColorBrushProperty, value);
    }

    // ──────────────────────────────────────────────
    //  Flag
    // ──────────────────────────────────────────────

    /// <summary>
    /// Defines the <see cref="IsFlagged"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsFlaggedProperty =
        AvaloniaProperty.Register<AssetTileControl, bool>(nameof(IsFlagged));

    /// <summary>
    /// Whether the asset is flagged.
    /// </summary>
    public bool IsFlagged
    {
        get => GetValue(IsFlaggedProperty);
        set => SetValue(IsFlaggedProperty, value);
    }

    // ──────────────────────────────────────────────
    //  Bindable text fields (3x)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Defines the <see cref="TextField1"/> property.
    /// </summary>
    public static readonly StyledProperty<string> TextField1Property =
        AvaloniaProperty.Register<AssetTileControl, string>(nameof(TextField1), string.Empty);

    /// <summary>
    /// Defines the <see cref="TextField1Label"/> property.
    /// </summary>
    public static readonly StyledProperty<string> TextField1LabelProperty =
        AvaloniaProperty.Register<AssetTileControl, string>(nameof(TextField1Label), string.Empty);

    /// <summary>
    /// Defines the <see cref="TextField2"/> property.
    /// </summary>
    public static readonly StyledProperty<string> TextField2Property =
        AvaloniaProperty.Register<AssetTileControl, string>(nameof(TextField2), string.Empty);

    /// <summary>
    /// Defines the <see cref="TextField2Label"/> property.
    /// </summary>
    public static readonly StyledProperty<string> TextField2LabelProperty =
        AvaloniaProperty.Register<AssetTileControl, string>(nameof(TextField2Label), string.Empty);

    /// <summary>
    /// Defines the <see cref="TextField3"/> property.
    /// </summary>
    public static readonly StyledProperty<string> TextField3Property =
        AvaloniaProperty.Register<AssetTileControl, string>(nameof(TextField3), string.Empty);

    /// <summary>
    /// Defines the <see cref="TextField3Label"/> property.
    /// </summary>
    public static readonly StyledProperty<string> TextField3LabelProperty =
        AvaloniaProperty.Register<AssetTileControl, string>(nameof(TextField3Label), string.Empty);

    /// <summary>
    /// First bindable text field value.
    /// </summary>
    public string TextField1
    {
        get => GetValue(TextField1Property);
        set => SetValue(TextField1Property, value);
    }

    /// <summary>
    /// Label for the first text field.
    /// </summary>
    public string TextField1Label
    {
        get => GetValue(TextField1LabelProperty);
        set => SetValue(TextField1LabelProperty, value);
    }

    /// <summary>
    /// Second bindable text field value.
    /// </summary>
    public string TextField2
    {
        get => GetValue(TextField2Property);
        set => SetValue(TextField2Property, value);
    }

    /// <summary>
    /// Label for the second text field.
    /// </summary>
    public string TextField2Label
    {
        get => GetValue(TextField2LabelProperty);
        set => SetValue(TextField2LabelProperty, value);
    }

    /// <summary>
    /// Third bindable text field value.
    /// </summary>
    public string TextField3
    {
        get => GetValue(TextField3Property);
        set => SetValue(TextField3Property, value);
    }

    /// <summary>
    /// Label for the third text field.
    /// </summary>
    public string TextField3Label
    {
        get => GetValue(TextField3LabelProperty);
        set => SetValue(TextField3LabelProperty, value);
    }

    // ──────────────────────────────────────────────
    //  Search highlight (T11.10)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Defines the <see cref="IsSearchHighlighted"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsSearchHighlightedProperty =
        AvaloniaProperty.Register<AssetTileControl, bool>(nameof(IsSearchHighlighted));

    /// <summary>
    /// True when this tile is part of an active FTS search result (T11.10).
    /// Enables the blue border highlight and matched-fields badge.
    /// </summary>
    public bool IsSearchHighlighted
    {
        get => GetValue(IsSearchHighlightedProperty);
        set => SetValue(IsSearchHighlightedProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="MatchedFieldsText"/> property.
    /// </summary>
    public static readonly StyledProperty<string> MatchedFieldsTextProperty =
        AvaloniaProperty.Register<AssetTileControl, string>(nameof(MatchedFieldsText), string.Empty);

    /// <summary>
    /// Comma-separated list of fields that matched the FTS query (e.g. "Title, Keywords").
    /// Displayed inside the tile when <see cref="IsSearchHighlighted"/> is true.
    /// </summary>
    public string MatchedFieldsText
    {
        get => GetValue(MatchedFieldsTextProperty);
        set => SetValue(MatchedFieldsTextProperty, value);
    }

    // ──────────────────────────────────────────────
    //  Toolbar actions
    // ──────────────────────────────────────────────

    /// <summary>
    /// Defines the <see cref="ToolbarActions"/> property.
    /// </summary>
    public static readonly DirectProperty<AssetTileControl, ObservableCollection<ToolbarAction>?> ToolbarActionsProperty =
        AvaloniaProperty.RegisterDirect<AssetTileControl, ObservableCollection<ToolbarAction>?>(
            nameof(ToolbarActions),
            o => o.ToolbarActions,
            (o, v) => o.ToolbarActions = v);

    private ObservableCollection<ToolbarAction>? _toolbarActions;

    /// <summary>
    /// Collection of toolbar action buttons rendered above the thumbnail.
    /// Each action is displayed as an icon button with a tooltip.
    /// </summary>
    public ObservableCollection<ToolbarAction>? ToolbarActions
    {
        get => _toolbarActions;
        set => SetAndRaise(ToolbarActionsProperty, ref _toolbarActions, value);
    }
}
