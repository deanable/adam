using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.Shared.Extractors;
using Adam.Shared.Services;

namespace Adam.CatalogBrowser.ViewModels;

/// <summary>
/// ViewModel for the Plugin Manager dialog, showing loaded metadata extractors
/// (both built-in and third-party plugins) with status indicators.
/// </summary>
public sealed class PluginManagerViewModel : INotifyPropertyChanged
{
    private readonly PluginLoaderService _pluginLoader;
    private bool _isLoading;

    public PluginManagerViewModel(PluginLoaderService pluginLoader)
    {
        _pluginLoader = pluginLoader;
        Plugins = [];
        RefreshCommand = new RelayCommand(async _ => await RefreshAsync());
        OpenPluginFolderCommand = new RelayCommand(_ => OpenPluginFolder());
        CloseCommand = new RelayCommand(_ => RequestClose?.Invoke());

        LoadPlugins();
    }

    public ObservableCollection<PluginItem> Plugins { get; }

    public bool HasPlugins => Plugins.Count > 0;

    public string PluginDirectory => _pluginLoader.Extractors.Count > 0
        ? "(loaded)"
        : "No plugins loaded";

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public ICommand RefreshCommand { get; }
    public ICommand OpenPluginFolderCommand { get; }
    public ICommand CloseCommand { get; }

    /// <summary>
    /// Raised when the dialog should close.
    /// </summary>
    public event Action? RequestClose;

    private void LoadPlugins()
    {
        Plugins.Clear();

        foreach (var info in _pluginLoader.LoadedPlugins)
        {
            Plugins.Add(new PluginItem(
                Name: info.Name,
                AssemblyName: info.AssemblyName,
                Priority: info.Priority,
                IsBuiltIn: info.IsBuiltIn,
                Status: info.Status));
        }

        OnPropertyChanged(nameof(HasPlugins));
    }

    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            await _pluginLoader.ReloadAsync();
            LoadPlugins();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OpenPluginFolder()
    {
        // Open the plugin directory in the system file manager
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Adam", "plugins");

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start("explorer.exe", dir);
            else if (OperatingSystem.IsMacOS())
                System.Diagnostics.Process.Start("open", dir);
            else if (OperatingSystem.IsLinux())
                System.Diagnostics.Process.Start("xdg-open", dir);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open plugin folder: {ex.Message}");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Display model for a single plugin in the Plugin Manager list.
/// </summary>
public sealed record PluginItem(
    string Name,
    string AssemblyName,
    int Priority,
    bool IsBuiltIn,
    string Status);
