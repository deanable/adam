namespace Adam.CatalogBrowser.Services;

/// <summary>
/// Interface for ViewModels that raise a CloseRequested event
/// for modal navigation back to the previous view.
/// </summary>
public interface ICloseable
{
    /// <summary>
    /// Raised when the user wants to close this view and return to the previous one.
    /// </summary>
    event Action? CloseRequested;
}
