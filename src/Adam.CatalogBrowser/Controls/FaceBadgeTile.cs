using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Adam.CatalogBrowser.Controls;

/// <summary>
/// Small overlay badge showing face count on gallery thumbnails.
/// Auto-hides when count is 0. Clickable to open face tagging view.
/// Color-coded: blue for all named, gray for all unknown, orange for mixed.
/// </summary>
public sealed class FaceBadgeTile : ContentControl
{
    public static readonly StyledProperty<int> FaceCountProperty =
        AvaloniaProperty.Register<FaceBadgeTile, int>(nameof(FaceCount));

    public static readonly StyledProperty<bool> HasNamedFacesProperty =
        AvaloniaProperty.Register<FaceBadgeTile, bool>(nameof(HasNamedFaces));

    public static readonly StyledProperty<bool> HasUnknownFacesProperty =
        AvaloniaProperty.Register<FaceBadgeTile, bool>(nameof(HasUnknownFaces));

    public int FaceCount
    {
        get => GetValue(FaceCountProperty);
        set => SetValue(FaceCountProperty, value);
    }

    /// <summary>True when at least one face is assigned to a known person.</summary>
    public bool HasNamedFaces
    {
        get => GetValue(HasNamedFacesProperty);
        set => SetValue(HasNamedFacesProperty, value);
    }

    /// <summary>True when at least one face is unknown (not yet named).</summary>
    public bool HasUnknownFaces
    {
        get => GetValue(HasUnknownFacesProperty);
        set => SetValue(HasUnknownFacesProperty, value);
    }

    public bool HasVisibleFaces => FaceCount > 0;

    /// <summary>
    /// Returns the badge color based on face assignment status.
    /// Blue (#0D9488) = all named, Gray (#6B7280) = all unknown, Orange (#F59E0B) = mixed.
    /// </summary>
    public IBrush BadgeColor
    {
        get
        {
            if (HasNamedFaces && HasUnknownFaces)
                return new SolidColorBrush(Color.Parse("#F59E0B")); // Orange for mixed
            if (HasNamedFaces)
                return new SolidColorBrush(Color.Parse("#0D9488")); // Teal for named
            return new SolidColorBrush(Color.Parse("#6B7280")); // Gray for unknown
        }
    }

    /// <summary>Text to display on the badge: e.g. "👤 3".</summary>
    public string BadgeText => $"👤 {FaceCount}";
}
