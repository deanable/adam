using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Adam.CatalogBrowser.Controls;

/// <summary>
/// Represents a single action button in the AssetTileControl toolbar.
/// Contains icon text, tooltip, and command for binding.
/// </summary>
public class ToolbarAction : AvaloniaObject
{
    /// <summary>
    /// Defines the <see cref="Icon"/> property.
    /// </summary>
    public static readonly StyledProperty<string> IconProperty =
        AvaloniaProperty.Register<ToolbarAction, string>(nameof(Icon), "\u2022");

    /// <summary>
    /// Defines the <see cref="ToolTipText"/> property.
    /// </summary>
    public static readonly StyledProperty<string> ToolTipTextProperty =
        AvaloniaProperty.Register<ToolbarAction, string>(nameof(ToolTipText), string.Empty);

    /// <summary>
    /// Defines the <see cref="Command"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<ToolbarAction, ICommand?>(nameof(Command));

    /// <summary>
    /// Icon text or unicode character displayed on the button.
    /// </summary>
    public string Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// Tooltip text shown on hover.
    /// </summary>
    public string ToolTipText
    {
        get => GetValue(ToolTipTextProperty);
        set => SetValue(ToolTipTextProperty, value);
    }

    /// <summary>
    /// Command to execute when the button is clicked.
    /// </summary>
    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
}
