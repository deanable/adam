using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.Shared.Services;
using LiquidVision.Core;
using LiquidVision.Core.Configuration;

namespace Adam.CatalogBrowser.ViewModels;

/// <summary>
/// ViewModel for the AI model selector in the sidebar.
/// Displays available model variants with sizes and provides a download/initialize command.
/// </summary>
public sealed class AiModelSelectorViewModel : INotifyPropertyChanged
{
    private readonly AiTaggingService? _aiTaggingService;
    private readonly LiquidVisionOptions _options;
    private AiModelDefinition? _selectedModel;
    private ExecutionProviderKind _selectedExecutionProvider;
    private int _gpuDeviceId;
    private bool _isModelDownloading;
    private double _modelDownloadProgress;
    private string _modelStatus = string.Empty;
    private bool _restartRequired;

    /// <summary>Available execution provider options for the dropdown.</summary>
    public ObservableCollection<ExecutionProviderOption> ProviderOptions { get; } =
    [
        new() { Kind = ExecutionProviderKind.Cpu, DisplayName = "CPU" },
        new() { Kind = ExecutionProviderKind.Cuda, DisplayName = "CUDA (NVIDIA)" },
        new() { Kind = ExecutionProviderKind.DirectML, DisplayName = "DirectML (Windows)" }
    ];

    public AiModelSelectorViewModel(
        AiTaggingService? aiTaggingService,
        LiquidVisionOptions options)
    {
        _aiTaggingService = aiTaggingService;
        _options = options;

        // Populate model options from the built-in list
        foreach (var model in AiModelDefinition.All)
            AvailableModels.Add(model);

        // Select the currently configured model
        var currentMatch = AiModelDefinition.FindOrDefault(_options.ModelId, _options.Precision);
        _selectedModel = currentMatch;

        // Select the currently configured execution provider
        _selectedExecutionProvider = options.ExecutionProvider;
        _gpuDeviceId = options.GpuDeviceId;

        // Wire model download progress from AiTaggingService
        if (aiTaggingService != null)
        {
            aiTaggingService.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AiTaggingService.DownloadProgress))
                {
                    ModelDownloadProgress = aiTaggingService.DownloadProgress;
                    if (aiTaggingService.DownloadProgress > 0 && aiTaggingService.DownloadProgress < 1.0)
                    {
                        IsModelDownloading = true;
                        ModelStatus = $"Downloading model... {aiTaggingService.DownloadProgress * 100:F0}%";
                    }
                    else if (aiTaggingService.DownloadProgress >= 1.0)
                    {
                        IsModelDownloading = false;
                        ModelDownloadProgress = 0;
                        ModelStatus = "Model ready";
                    }
                }
                else if (e.PropertyName == nameof(AiTaggingService.IsInitialized))
                {
                    if (aiTaggingService.IsInitialized)
                    {
                        IsModelDownloading = false;
                        ModelDownloadProgress = 0;
                        ModelStatus = "Model ready";
                        OnPropertyChanged(nameof(IsModelReady));
                    }
                }
            };
        }

        // Initialize status
        ModelStatus = aiTaggingService?.IsInitialized == true
            ? "Model ready"
            : "Select a model and download";

        DownloadOrApplyCommand = new RelayCommand(async _ => await DownloadOrApplyModelAsync(),
            _ => SelectedModel != null && !IsModelDownloading);
    }

    /// <summary>Available AI model variants for selection.</summary>
    public ObservableCollection<AiModelDefinition> AvailableModels { get; } = [];

    /// <summary>Currently selected model variant.</summary>
    public AiModelDefinition? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (_selectedModel == value) return;
            _selectedModel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedModelDisplay));
            OnPropertyChanged(nameof(IsModelSelected));
            (DownloadOrApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();

            // If a different model is selected, mark restart needed for non-active model
            if (value != null && !IsCurrentModel(value))
            {
                RestartRequired = true;
            }
        }
    }

    /// <summary>Combined display string for the current selection.</summary>
    public string SelectedModelDisplay => SelectedModel?.DisplayLabel ?? "None";

    /// <summary>True when a model is selected.</summary>
    public bool IsModelSelected => SelectedModel != null;

    /// <summary>True when the currently selected model is the one that's configured and ready.</summary>
    public bool IsModelReady => _aiTaggingService?.IsInitialized == true;

    /// <summary>True while a model download is in progress.</summary>
    public bool IsModelDownloading
    {
        get => _isModelDownloading;
        set
        {
            if (_isModelDownloading == value) return;
            _isModelDownloading = value;
            OnPropertyChanged();
            (DownloadOrApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Download progress (0.0 to 1.0).</summary>
    public double ModelDownloadProgress
    {
        get => _modelDownloadProgress;
        set { _modelDownloadProgress = value; OnPropertyChanged(); }
    }

    /// <summary>Status text shown below the model selector.</summary>
    public string ModelStatus
    {
        get => _modelStatus;
        set { _modelStatus = value; OnPropertyChanged(); }
    }

    /// <summary>True when the app must be restarted for the model change to take effect.</summary>
    public bool RestartRequired
    {
        get => _restartRequired;
        set { _restartRequired = value; OnPropertyChanged(); }
    }

    /// <summary>Currently selected execution provider option (for XAML binding).</summary>
    public ExecutionProviderOption? SelectedProviderOption
    {
        get => ProviderOptions.FirstOrDefault(p => p.Kind == _selectedExecutionProvider);
        set
        {
            if (value == null || value.Kind == _selectedExecutionProvider) return;
            _selectedExecutionProvider = value.Kind;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGpuProvider));

            // Mark restart needed
            RestartRequired = true;

            // Persist immediately
            var config = App.Config;
            config.AiExecutionProvider = value.Kind.ToString();
            config.Save();

            // Update runtime options
            _options.ExecutionProvider = value.Kind;
        }
    }

    /// <summary>True when the selected provider is a GPU provider (shows device ID field).</summary>
    public bool IsGpuProvider =>
        _selectedExecutionProvider is ExecutionProviderKind.Cuda or ExecutionProviderKind.DirectML;

    /// <summary>GPU device ID (0 = first GPU).</summary>
    public int GpuDeviceId
    {
        get => _gpuDeviceId;
        set
        {
            if (_gpuDeviceId == value) return;
            _gpuDeviceId = value;
            OnPropertyChanged();

            // Persist immediately
            var config = App.Config;
            config.AiGpuDeviceId = value;
            config.Save();

            // Update runtime options
            _options.GpuDeviceId = value;
        }
    }

    public ICommand DownloadOrApplyCommand { get; }

    /// <summary>
    /// True if the given definition matches the currently configured model.
    /// </summary>
    private bool IsCurrentModel(AiModelDefinition def) =>
        def.ModelId.Equals(_options.ModelId, System.StringComparison.OrdinalIgnoreCase) &&
        def.Precision == _options.Precision;

    /// <summary>
    /// Saves the selected model to config, triggers download, and reinitializes the analyzer.
    /// </summary>
    private async Task DownloadOrApplyModelAsync()
    {
        if (SelectedModel == null) return;

        // Save to config so it persists
        var config = App.Config;
        config.AiModelId = SelectedModel.ModelId;
        config.AiPrecision = SelectedModel.Precision.ToString();
        config.AiExecutionProvider = _selectedExecutionProvider.ToString();
        config.AiGpuDeviceId = _gpuDeviceId;
        config.Save();

        // Update the runtime options
        _options.ModelId = SelectedModel.ModelId;
        _options.Precision = SelectedModel.Precision;
        _options.ExecutionProvider = _selectedExecutionProvider;
        _options.GpuDeviceId = _gpuDeviceId;

        // Trigger download+initialize if the AI tagging service is available
        if (_aiTaggingService != null)
        {
            try
            {
                ModelStatus = "Initializing model...";
                await _aiTaggingService.EnsureInitializedAsync(null, CancellationToken.None);
                ModelStatus = "Model ready";
                RestartRequired = false;
            }
            catch (Exception ex)
            {
                ModelStatus = $"Error: {ex.Message}";
            }
        }
        else
        {
            // AI tagging service is not available yet — mark restart required
            ModelStatus = "Model saved. Restart to apply.";
            RestartRequired = true;
        }

        OnPropertyChanged(nameof(IsModelReady));
        (DownloadOrApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Describes an execution provider option for the dropdown selector.
/// </summary>
public sealed class ExecutionProviderOption
{
    /// <summary>The execution provider kind.</summary>
    public ExecutionProviderKind Kind { get; set; }

    /// <summary>User-friendly display name for the dropdown.</summary>
    public string DisplayName { get; set; } = string.Empty;
}
