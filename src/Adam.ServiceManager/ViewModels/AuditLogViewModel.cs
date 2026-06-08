using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.ServiceManager.Services;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.ServiceManager.ViewModels;

public class AuditLogItem : INotifyPropertyChanged
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class AuditLogViewModel : INotifyPropertyChanged
{
    private readonly ILogger<AuditLogViewModel> _logger;
    private readonly ModeManager _modeManager;
    private readonly IUiDispatcher _dispatcher;
    private string _statusText = string.Empty;
    private bool _isLoading;
    private string _filterAction = string.Empty;
    private string _filterEntityType = string.Empty;
    private DateTimeOffset? _filterFrom;
    private DateTimeOffset? _filterTo;
    private ObservableCollection<string> _logMessages = [];

    public AuditLogViewModel(ModeManager modeManager, ILogger<AuditLogViewModel>? logger = null, IUiDispatcher? dispatcher = null)
    {
        _logger = logger ?? NullLogger<AuditLogViewModel>.Instance;
        _modeManager = modeManager ?? throw new ArgumentNullException(nameof(modeManager));
        _dispatcher = dispatcher ?? new AvaloniaUiDispatcher();

        RefreshCommand = new RelayCommand(async _ => await LoadLogsAsync());
        ClearLogCommand = new RelayCommand(_ => LogMessages.Clear());

        _logMessages.Add($"[{DateTime.Now:HH:mm:ss.fff}] Audit Log initialized");
    }

    public ObservableCollection<AuditLogItem> Logs { get; } = [];

    public ICommand RefreshCommand { get; }
    public ICommand ClearLogCommand { get; }

    public ObservableCollection<string> LogMessages
    {
        get => _logMessages;
        set { _logMessages = value; OnPropertyChanged(); }
    }

    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }
    public string FilterAction { get => _filterAction; set { _filterAction = value; OnPropertyChanged(); } }
    public string FilterEntityType { get => _filterEntityType; set { _filterEntityType = value; OnPropertyChanged(); } }
    public DateTimeOffset? FilterFrom { get => _filterFrom; set { _filterFrom = value; OnPropertyChanged(); } }
    public DateTimeOffset? FilterTo { get => _filterTo; set { _filterTo = value; OnPropertyChanged(); } }

    private void AddLog(string message)
    {
        _logger.LogInformation("[AuditLog] {Message}", message);
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var entry = $"[{timestamp}] {message}";

        if (_dispatcher.CheckAccess())
        {
            AddLogEntry(entry);
        }
        else
        {
            _dispatcher.Post(() => AddLogEntry(entry));
        }
    }

    private void AddLogEntry(string entry)
    {
        if (_logMessages.Count > 500)
            _logMessages.RemoveAt(0);
        _logMessages.Add(entry);
    }

    private async Task RunOnUiThreadAsync(Action action)
    {
        if (_dispatcher.CheckAccess())
            action();
        else
            await _dispatcher.InvokeAsync(action);
    }

    public async Task LoadLogsAsync()
    {
        AddLog("Loading audit logs...");
        await RunOnUiThreadAsync(() => IsLoading = true);

        try
        {
            AddLog("Querying database...");
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            await RunOnUiThreadAsync(() => Logs.Clear());

            // Apply server-side filters only for string/guid columns (SQLite can't handle DateTimeOffset in WHERE/ORDER BY)
            var query = db.AccessLogs.Include(l => l.User).AsQueryable();

            if (!string.IsNullOrEmpty(FilterAction))
            {
                query = query.Where(l => l.Action == FilterAction);
                AddLog($"Filtering by action: '{FilterAction}'");
            }
            if (!string.IsNullOrEmpty(FilterEntityType))
            {
                query = query.Where(l => l.EntityType == FilterEntityType);
                AddLog($"Filtering by entity type: '{FilterEntityType}'");
            }

            AddLog("Fetching records...");
            var logs = await query.Take(500).ToListAsync().ConfigureAwait(false);

            // Apply DateTimeOffset filters and ordering on the client side (SQLite limitation)
            if (FilterFrom.HasValue)
            {
                var from = FilterFrom.Value;
                logs = logs.Where(l => l.Timestamp >= from).ToList();
                AddLog($"Filtered from: {from:yyyy-MM-dd HH:mm} -> {logs.Count} records remaining");
            }
            if (FilterTo.HasValue)
            {
                var to = FilterTo.Value;
                logs = logs.Where(l => l.Timestamp <= to).ToList();
                AddLog($"Filtered to: {to:yyyy-MM-dd HH:mm} -> {logs.Count} records remaining");
            }

            logs = [.. logs.OrderByDescending(l => l.Timestamp)];

            foreach (var l in logs)
            {
                await RunOnUiThreadAsync(() =>
                {
                    Logs.Add(new AuditLogItem
                    {
                        Id = l.Id,
                        Username = l.User?.Username ?? "",
                        Action = l.Action,
                        EntityType = l.EntityType,
                        EntityId = l.EntityId?.ToString() ?? "",
                        Details = l.Details ?? "",
                        Timestamp = l.Timestamp
                    });
                });
            }
            AddLog($"Loaded {logs.Count} log entr{(logs.Count == 1 ? "y" : "ies")} from database.");
            await RunOnUiThreadAsync(() => StatusText = $"{Logs.Count} log entr{(Logs.Count == 1 ? "y" : "ies")}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load audit logs");
            AddLog($"ERROR loading audit logs: {ex.GetType().Name}: {ex.Message}");
            await RunOnUiThreadAsync(() => StatusText = $"Error: {ex.Message}");
        }
        finally
        {
            await RunOnUiThreadAsync(() => IsLoading = false);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
