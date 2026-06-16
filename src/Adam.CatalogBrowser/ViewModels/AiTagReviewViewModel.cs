using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.Shared.Services;

namespace Adam.CatalogBrowser.ViewModels;

/// <summary>
/// ViewModel for the AI tag review dialog.
/// Displays AI-suggested keywords and categories with confidence indicators,
/// allows accept/reject per item, and supports "Accept all above threshold".
/// </summary>
public class AiTagReviewViewModel : INotifyPropertyChanged
{
    private double _confidenceThreshold = 0.6;
    private int _acceptedCount;
    private int _totalCount;

    public AiTagReviewViewModel(AiTagResult result)
    {
        Result = result;

        // Populate keyword scores, sorted by confidence descending
        foreach (var kw in result.Keywords.OrderByDescending(k => k.Confidence))
        {
            kw.IsAccepted = kw.Confidence >= ConfidenceThreshold;
            kw.PropertyChanged += (_, _) => RecalculateCounts();
            KeywordScores.Add(kw);
        }

        foreach (var cat in result.Categories.OrderByDescending(c => c.Confidence))
        {
            cat.IsAccepted = cat.Confidence >= ConfidenceThreshold;
            cat.PropertyChanged += (_, _) => RecalculateCounts();
            CategoryScores.Add(cat);
        }

        RecalculateCounts();

        AcceptAllAboveThresholdCommand = new RelayCommand(_ => ApplyThreshold());
        ApplyCommand = new RelayCommand(_ => CloseWithResult(true));
        CancelCommand = new RelayCommand(_ => CloseWithResult(false));
    }

    public AiTagResult Result { get; }

    public ObservableCollection<KeywordScore> KeywordScores { get; } = [];
    public ObservableCollection<CategoryScore> CategoryScores { get; } = [];

    /// <summary>
    /// Confidence threshold slider value (0.0 to 1.0).
    /// Items below this threshold are unchecked by default.
    /// </summary>
    public double ConfidenceThreshold
    {
        get => _confidenceThreshold;
        set
        {
            if (Math.Abs(_confidenceThreshold - value) < 0.01) return;
            _confidenceThreshold = Math.Round(value, 2);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConfidenceThresholdPercent));
            ApplyThreshold();
        }
    }

    /// <summary>
    /// Threshold displayed as a percentage string (e.g. "60%").
    /// </summary>
    public string ConfidenceThresholdPercent => $"{_confidenceThreshold * 100:F0}%";

    /// <summary>
    /// True if the dialog's result should be committed (Apply clicked).
    /// False if cancelled.
    /// </summary>
    public bool? DialogResult { get; private set; }

    public int AcceptedCount
    {
        get => _acceptedCount;
        set { _acceptedCount = value; OnPropertyChanged(); }
    }

    public int TotalCount
    {
        get => _totalCount;
        set { _totalCount = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Summary text like "Accepted 8 of 12 tags".
    /// </summary>
    public string AcceptanceSummary => $"Accepted {AcceptedCount} of {TotalCount} tags";

    /// <summary>
    /// Returns a formatted confidence bar width (0-100%).
    /// </summary>
    public static double ConfidenceToBarWidth(double confidence) => confidence * 100;

    /// <summary>
    /// Returns a color hex for the confidence bar.
    /// </summary>
    public static string ConfidenceToColor(double confidence) => confidence switch
    {
        >= 0.8 => "#2E7D32",  // Green — high
        >= 0.5 => "#F57F17",  // Yellow — medium
        _ => "#C62828"         // Red — low
    };

    public ICommand AcceptAllAboveThresholdCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand CancelCommand { get; }

    /// <summary>
    /// Event raised when the dialog should close.
    /// Payload is the accepted keyword/category names.
    /// </summary>
    public event Action<bool>? CloseRequested;

    private void CloseWithResult(bool accepted)
    {
        DialogResult = accepted;
        CloseRequested?.Invoke(accepted);
    }

    /// <summary>
    /// Applies the current confidence threshold to all items.
    /// Items with confidence >= threshold are accepted; others are rejected.
    /// </summary>
    private void ApplyThreshold()
    {
        foreach (var kw in KeywordScores)
            kw.IsAccepted = kw.Confidence >= ConfidenceThreshold;
        foreach (var cat in CategoryScores)
            cat.IsAccepted = cat.Confidence >= ConfidenceThreshold;
        RecalculateCounts();
    }

    /// <summary>
    /// Toggles the accepted state of a keyword and recalculates counts.
    /// Called when the user clicks a checkbox.
    /// </summary>
    public void ToggleKeyword(KeywordScore kw)
    {
        kw.IsAccepted = !kw.IsAccepted;
        RecalculateCounts();
    }

    /// <summary>
    /// Toggles the accepted state of a category.
    /// </summary>
    public void ToggleCategory(CategoryScore cat)
    {
        cat.IsAccepted = !cat.IsAccepted;
        RecalculateCounts();
    }

    private void RecalculateCounts()
    {
        var accepted = KeywordScores.Count(k => k.IsAccepted)
                     + CategoryScores.Count(c => c.IsAccepted);
        var total = KeywordScores.Count + CategoryScores.Count;
        AcceptedCount = accepted;
        TotalCount = total;
        OnPropertyChanged(nameof(AcceptanceSummary));
    }

    /// <summary>
    /// Returns the list of accepted keyword names.
    /// </summary>
    public List<string> GetAcceptedKeywords() =>
        KeywordScores.Where(k => k.IsAccepted).Select(k => k.Name).ToList();

    /// <summary>
    /// Returns the list of accepted category names.
    /// </summary>
    public List<string> GetAcceptedCategories() =>
        CategoryScores.Where(c => c.IsAccepted).Select(c => c.Name).ToList();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
