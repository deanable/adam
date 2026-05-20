using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Adam.CatalogBrowser.Controls;

/// <summary>
/// A custom templated control for displaying an asset as a horizontal row in
/// the gallery list view.  Layout (left to right):
///   [Thumbnail] [FileName / Type] [Size] [Dimensions] [Date]
/// </summary>
public class AssetListRowControl : TemplatedControl
{
    // ──────────────────────────────────────────────
    //  Thumbnail
    // ──────────────────────────────────────────────

    public static readonly StyledProperty<IImage?> ThumbnailProperty =
        AvaloniaProperty.Register<AssetListRowControl, IImage?>(nameof(Thumbnail));

    public static readonly StyledProperty<double> ThumbnailSizeProperty =
        AvaloniaProperty.Register<AssetListRowControl, double>(nameof(ThumbnailSize), 40.0);

    public IImage? Thumbnail
    {
        get => GetValue(ThumbnailProperty);
        set => SetValue(ThumbnailProperty, value);
    }

    public double ThumbnailSize
    {
        get => GetValue(ThumbnailSizeProperty);
        set => SetValue(ThumbnailSizeProperty, value);
    }

    // ──────────────────────────────────────────────
    //  Text fields
    // ──────────────────────────────────────────────

    public static readonly StyledProperty<string> FileNameProperty =
        AvaloniaProperty.Register<AssetListRowControl, string>(nameof(FileName), string.Empty);

    public static readonly StyledProperty<string> FileTypeProperty =
        AvaloniaProperty.Register<AssetListRowControl, string>(nameof(FileType), string.Empty);

    public static readonly StyledProperty<string> FileSizeFormattedProperty =
        AvaloniaProperty.Register<AssetListRowControl, string>(nameof(FileSizeFormatted), string.Empty);

    public static readonly StyledProperty<string> DimensionsProperty =
        AvaloniaProperty.Register<AssetListRowControl, string>(nameof(Dimensions), string.Empty);

    public static readonly StyledProperty<string> CreatedAtFormattedProperty =
        AvaloniaProperty.Register<AssetListRowControl, string>(nameof(CreatedAtFormatted), string.Empty);

    public string FileName
    {
        get => GetValue(FileNameProperty);
        set => SetValue(FileNameProperty, value);
    }

    public string FileType
    {
        get => GetValue(FileTypeProperty);
        set => SetValue(FileTypeProperty, value);
    }

    public string FileSizeFormatted
    {
        get => GetValue(FileSizeFormattedProperty);
        set => SetValue(FileSizeFormattedProperty, value);
    }

    public string Dimensions
    {
        get => GetValue(DimensionsProperty);
        set => SetValue(DimensionsProperty, value);
    }

    public string CreatedAtFormatted
    {
        get => GetValue(CreatedAtFormattedProperty);
        set => SetValue(CreatedAtFormattedProperty, value);
    }
}
