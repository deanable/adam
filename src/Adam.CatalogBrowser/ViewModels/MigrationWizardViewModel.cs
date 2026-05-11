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

    public MigrationWizardViewModel(ModeManager modeManager, IDbMigrationService? migrationService = null)
    {
        _modeManager = modeManager;
        _migrationService = migrationService;
        SourcePath = modeManager.DbPath;

        StartMigrationCommand = new RelayCommand(async _ => await StartMigrationAsync(), _ => !IsBusy);
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

            _migrationService.Progress += OnMigrationProgress;
            await _migrationService.MigrateAsync(SourcePath, SelectedTargetProvider, connString);
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
