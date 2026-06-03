using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Services;

namespace Adam.CatalogBrowser.ViewModels;

public class MigrationWizardViewModel : INotifyPropertyChanged
{
    private readonly ModeManager _modeManager;
    private readonly IDbMigrationService? _migrationService;
    private string _sourcePath = string.Empty;
    private string _selectedTargetProvider = "sqlite";
    private string _targetConnectionString = string.Empty;
    private bool _isBusy;
    private string _statusText = string.Empty;
    private int _progressValue;
    private CancellationTokenSource? _migrationCts;

    public MigrationWizardViewModel(ModeManager modeManager, IDbMigrationService? migrationService = null)
    {
        _modeManager = modeManager;
        _migrationService = migrationService;
        SourcePath = modeManager.DbPath;

        StartMigrationCommand = new RelayCommand(async _ => await StartMigrationAsync(), _ => !IsBusy);
        CancelMigrationCommand = new RelayCommand(_ => CancelMigration(), _ => IsBusy);
        BrowseSourceCommand = new RelayCommand(async _ => await BrowseSourceAsync());
    }

    public ObservableCollection<MigrationLogEntry> Log { get; } = [];

    public string SourcePath
    {
        get => _sourcePath;
        set { _sourcePath = value; OnPropertyChanged(); }
    }

    public string SelectedTargetProvider
    {
        get => _selectedTargetProvider;
        set { _selectedTargetProvider = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsProviderSqlite)); }
    }

    public bool IsProviderSqlite => SelectedTargetProvider == "sqlite";

    public string TargetConnectionString
    {
        get => _targetConnectionString;
        set { _targetConnectionString = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public int ProgressValue
    {
        get => _progressValue;
        set { _progressValue = value; OnPropertyChanged(); }
    }

    public ICommand StartMigrationCommand { get; }
    public ICommand CancelMigrationCommand { get; }
    public ICommand BrowseSourceCommand { get; }

    private async Task StartMigrationAsync()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            StatusText = "Please select a source database file.";
            return;
        }

        if (_migrationService == null)
        {
            StatusText = "Migration service not available.";
            return;
        }

        IsBusy = true;
        Log.Clear();
        ProgressValue = 0;
        StatusText = "Starting migration...";

        try
        {
            var connString = SelectedTargetProvider == "sqlite"
                ? TargetConnectionString
                : TargetConnectionString;

            if (string.IsNullOrWhiteSpace(connString))
            {
                StatusText = "Please enter a target connection string.";
                IsBusy = false;
                return;
            }

            _migrationCts?.Dispose();
            _migrationCts = new CancellationTokenSource();
            _migrationService.Progress += OnMigrationProgress;
            await _migrationService.MigrateAsync(SourcePath, SelectedTargetProvider, connString, _migrationCts.Token);
            _migrationService.Progress -= OnMigrationProgress;

            StatusText = "Migration completed successfully.";
            ProgressValue = 100;
        }
        catch (Exception ex)
        {
            StatusText = $"Migration failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CancelMigration()
    {
        _migrationCts?.Cancel();
        StatusText = "Migration cancelled.";
    }

    private async Task BrowseSourceAsync()
    {
        try
        {
            // Use StorageProvider API for file dialog
            var mainWindow = App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
            if (mainWindow?.StorageProvider == null)
            {
                StatusText = "File dialog not available.";
                return;
            }

            var result = await mainWindow.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select SQLite Database",
                FileTypeFilter =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType("SQLite Databases")
                    {
                        Patterns = ["*.db", "*.sqlite", "*.sqlite3"]
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("All Files")
                    {
                        Patterns = ["*"]
                    }
                ]
            });

            if (result?.Count > 0)
            {
                SourcePath = result[0].Path.LocalPath;
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to browse: {ex.Message}";
        }
    }

    private void OnMigrationProgress(object? sender, MigrationProgressEventArgs e)
    {
        Log.Add(new MigrationLogEntry
        {
            Table = e.Table,
            Message = e.Message,
            Rows = e.RowsMigrated,
            Total = e.TotalRows
        });
        if (e.TotalRows > 0)
            ProgressValue = (int)((double)e.RowsMigrated / e.TotalRows * 100);
        StatusText = e.Message;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class MigrationLogEntry
{
    public string Table { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public int Rows { get; init; }
    public int Total { get; init; }
}
