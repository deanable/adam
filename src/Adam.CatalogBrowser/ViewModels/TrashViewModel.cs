using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Models;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Services;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.ViewModels;

public class DeletedAssetItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public DateTimeOffset DeletedAt { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public string DeletedAtFormatted => DeletedAt.ToLocalTime().ToString("g");

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class TrashViewModel : INotifyPropertyChanged
{
    private readonly DeleteService _deleteService;
    private readonly ToastService _toastService;
    private readonly ILogger<TrashViewModel> _logger;
    private bool _isLoading;
    private bool _hasItems;
    private bool _isProcessing;

    public TrashViewModel(
        DeleteService deleteService,
        ToastService toastService,
        ILogger<TrashViewModel> logger)
    {
        _deleteService = deleteService;
        _toastService = toastService;
        _logger = logger;

        RestoreSelectedCommand = new RelayCommand(async _ => await RestoreSelectedAsync(), _ => SelectedItems.Any() && !IsProcessing);
        PermanentlyDeleteSelectedCommand = new RelayCommand(async _ => await PermanentlyDeleteSelectedAsync(), _ => SelectedItems.Any() && !IsProcessing);
        RefreshCommand = new RelayCommand(async _ => await LoadDeletedAssetsAsync());
    }

    public ObservableCollection<DeletedAssetItem> DeletedAssets { get; } = [];
    public ObservableCollection<DeletedAssetItem> SelectedItems { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public bool HasItems
    {
        get => _hasItems;
        set { _hasItems = value; OnPropertyChanged(); }
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set { _isProcessing = value; OnPropertyChanged(); }
    }

    public ICommand RestoreSelectedCommand { get; }
    public ICommand PermanentlyDeleteSelectedCommand { get; }
    public ICommand RefreshCommand { get; }

    public async Task LoadDeletedAssetsAsync()
    {
        IsLoading = true;
        try
        {
            var deleted = await _deleteService.GetDeletedAssetsAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                DeletedAssets.Clear();
                foreach (var asset in deleted)
                {
                    DeletedAssets.Add(new DeletedAssetItem
                    {
                        Id = asset.Id,
                        FileName = asset.FileName,
                        FileType = asset.MimeType,
                        StoragePath = asset.StoragePath,
                        DeletedAt = asset.ModifiedAt
                    });
                }
                HasItems = DeletedAssets.Count > 0;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load deleted assets");
            _toastService.Show("Failed to load deleted assets", Services.ToastLevel.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Called from code-behind when selection changes.
    /// </summary>
    public void UpdateSelection(IList<object?> selectedItems)
    {
        SelectedItems.Clear();
        foreach (var item in selectedItems.OfType<DeletedAssetItem>())
            SelectedItems.Add(item);

        (RestoreSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (PermanentlyDeleteSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task RestoreSelectedAsync()
    {
        var items = SelectedItems.ToList();
        if (items.Count == 0) return;

        IsProcessing = true;
        int succeeded = 0, failed = 0;
        try
        {
            foreach (var item in items)
            {
                try
                {
                    if (await _deleteService.RestoreAsync(item.Id))
                        succeeded++;
                    else
                        failed++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to restore asset {Id}", item.Id);
                    failed++;
                }
            }

            _toastService.Show($"Restored {succeeded} asset(s)" + (failed > 0 ? $" ({failed} failed)" : ""),
                failed > 0 ? Services.ToastLevel.Warning : Services.ToastLevel.Success);

            await LoadDeletedAssetsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during restore operation");
            _toastService.Show("Restore operation failed", Services.ToastLevel.Error);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private async Task PermanentlyDeleteSelectedAsync()
    {
        var items = SelectedItems.ToList();
        if (items.Count == 0) return;

        // Show confirmation dialog before permanent deletion
        var owner = App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (owner == null) return;

        var message = items.Count == 1
            ? $"Are you sure you want to permanently delete '{items[0].FileName}'?\n\nThis action cannot be undone. The file will be permanently removed from the database."
            : $"Are you sure you want to permanently delete {items.Count} selected assets?\n\nThis action cannot be undone. All files will be permanently removed from the database.";

        var confirmed = await Views.ConfirmationDialog.ShowAsync(owner,
            "Permanently Delete", message, "Permanently Delete", "Cancel", isDestructive: true);

        if (!confirmed) return;

        IsProcessing = true;
        int succeeded = 0, failed = 0;
        try
        {
            foreach (var item in items)
            {
                try
                {
                    if (await _deleteService.PermanentlyDeleteAsync(item.Id))
                        succeeded++;
                    else
                        failed++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to permanently delete asset {Id}", item.Id);
                    failed++;
                }
            }

            _toastService.Show($"Permanently deleted {succeeded} asset(s)" + (failed > 0 ? $" ({failed} failed)" : ""),
                failed > 0 ? Services.ToastLevel.Warning : Services.ToastLevel.Success);

            await LoadDeletedAssetsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during permanent delete operation");
            _toastService.Show("Permanent delete operation failed", Services.ToastLevel.Error);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
