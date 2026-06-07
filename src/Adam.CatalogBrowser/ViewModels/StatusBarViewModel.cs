using System.ComponentModel;
using System.Runtime.CompilerServices;
using Adam.CatalogBrowser.Services;
using Avalonia.Threading;

namespace Adam.CatalogBrowser.ViewModels;

public class StatusBarViewModel : INotifyPropertyChanged
{
    private string _statusText = "Ready";
    private bool _isBusy;
    private bool _isInitialLoading;
    private string _bulkProgressText = string.Empty;
    private bool _isBulkOperationActive;
    private double _bulkProgressPercentage;

    // AI model download progress (D-14)
    private bool _isModelDownloading;
    private double _modelDownloadPercentage;

    // AI tagging progress (Trigger C)
    private bool _isAiTaggingActive;
    private double _aiTaggingPercentage;
    private string _aiTaggingProgressText = string.Empty;

    public StatusBarViewModel(BulkOperationQueue bulkQueue)
    {
        bulkQueue.ProgressChanged += (_, progress) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                BulkProgressPercentage = progress.Percentage;
                IsBulkOperationActive = progress.IsActive;
                BulkProgressText = progress.IsActive
                    ? $"Applying {progress.CurrentOperation}... ({progress.Completed}/{progress.Total})"
                    : string.Empty;
                if (!progress.IsActive && progress.Total > 0)
                {
                    StatusText = progress.Failed > 0
                        ? $"Bulk update complete: {progress.Completed} applied, {progress.Failed} failed"
                        : $"Bulk update complete: {progress.Completed} applied";
                }
            });
        };
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public bool IsInitialLoading
    {
        get => _isInitialLoading;
        set { _isInitialLoading = value; OnPropertyChanged(); }
    }

    public string BulkProgressText
    {
        get => _bulkProgressText;
        set { _bulkProgressText = value; OnPropertyChanged(); }
    }

    public bool IsBulkOperationActive
    {
        get => _isBulkOperationActive;
        set { _isBulkOperationActive = value; OnPropertyChanged(); }
    }

    public double BulkProgressPercentage
    {
        get => _bulkProgressPercentage;
        set { _bulkProgressPercentage = value; OnPropertyChanged(); }
    }

    // ── AI model download progress ──

    public bool IsModelDownloading
    {
        get => _isModelDownloading;
        set { _isModelDownloading = value; OnPropertyChanged(); }
    }

    public double ModelDownloadPercentage
    {
        get => _modelDownloadPercentage;
        set { _modelDownloadPercentage = value; OnPropertyChanged(); }
    }

    // ── AI tagging progress ──

    public bool IsAiTaggingActive
    {
        get => _isAiTaggingActive;
        set { _isAiTaggingActive = value; OnPropertyChanged(); }
    }

    public double AiTaggingPercentage
    {
        get => _aiTaggingPercentage;
        set { _aiTaggingPercentage = value; OnPropertyChanged(); }
    }

    public string AiTaggingProgressText
    {
        get => _aiTaggingProgressText;
        set { _aiTaggingProgressText = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
