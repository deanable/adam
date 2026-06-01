using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Contracts;
using Adam.Shared.Services;
using Avalonia.Threading;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.CatalogBrowser.ViewModels;

public class AuditLogViewModel : INotifyPropertyChanged
{
    private readonly ILogger<AuditLogViewModel> _logger;
    private readonly ModeManager _modeManager;
    private string _statusText = string.Empty;
    private bool _isLoading;
    private string _filterAction = string.Empty;
    private string _filterEntityType = string.Empty;
    private DateTimeOffset? _filterFrom;
    private DateTimeOffset? _filterTo;
    private ObservableCollection<string> _logMessages = [];

    public AuditLogViewModel(ModeManager modeManager, ILogger<AuditLogViewModel>? logger = null)
    {
        _logger = logger ?? NullLogger<AuditLogViewModel>.Instance;
        _modeManager = modeManager;
        RefreshCommand = new RelayCommand(async _ => await LoadLogsAsync());
        ExportCsvCommand = new RelayCommand(async _ => await ExportCsvAsync());
        ClearLogCommand = new RelayCommand(_ => LogMessages.Clear());

        _logMessages.Add($"[{DateTime.Now:HH:mm:ss.fff}] Audit Log initialized");
    }

    public ObservableCollection<AuditLogItem> Logs { get; } = [];

    public ICommand RefreshCommand { get; }
    public ICommand ExportCsvCommand { get; }
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

        if (Dispatcher.UIThread.CheckAccess())
        {
            AddLogEntry(entry);
        }
        else
        {
            Dispatcher.UIThread.Post(() => AddLogEntry(entry));
        }
    }

    private void AddLogEntry(string entry)
    {
        if (_logMessages.Count > 500)
            _logMessages.RemoveAt(0);
        _logMessages.Add(entry);
    }

    public async Task LoadLogsAsync()
    {
        AddLog("Loading audit logs...");
        IsLoading = true;
        Logs.Clear();

        try
        {
            if (_modeManager.IsStandalone)
            {
                AddLog("Standalone mode: querying SQLite database...");
                await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
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
                if (FilterFrom.HasValue)
                {
                    query = query.Where(l => l.Timestamp >= FilterFrom.Value);
                    AddLog($"Filtering from: {FilterFrom.Value:yyyy-MM-dd HH:mm}");
                }
                if (FilterTo.HasValue)
                {
                    query = query.Where(l => l.Timestamp <= FilterTo.Value);
                    AddLog($"Filtering to: {FilterTo.Value:yyyy-MM-dd HH:mm}");
                }

                var logs = await query.OrderByDescending(l => l.Timestamp).ToListAsync().ConfigureAwait(false);

                foreach (var l in logs)
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
                }
                AddLog($"Loaded {logs.Count} log entr{(logs.Count == 1 ? "y" : "ies")} from database.");
            }
            else if (_modeManager.IsMultiUser)
            {
                var broker = _modeManager.BrokerClient;
                var auth = _modeManager.AuthSession;
                if (broker == null || auth == null) return;

                if (!broker.IsConnected)
                {
                    AddLog("Multi-user mode: connecting to broker...");
                    await broker.ConnectAsync();
                }

                AddLog("Requesting audit log entries from broker...");
                var reqMsg = new ListAuditLogsRequest();
                if (!string.IsNullOrEmpty(FilterAction))
                {
                    reqMsg.Action = FilterAction;
                    AddLog($"Filtering by action: '{FilterAction}'");
                }
                if (!string.IsNullOrEmpty(FilterEntityType))
                {
                    reqMsg.EntityType = FilterEntityType;
                    AddLog($"Filtering by entity type: '{FilterEntityType}'");
                }
                if (FilterFrom.HasValue)
                {
                    reqMsg.FromDate = FilterFrom.Value.ToUnixTimeSeconds();
                    AddLog($"Filtering from: {FilterFrom.Value:yyyy-MM-dd HH:mm}");
                }
                if (FilterTo.HasValue)
                {
                    reqMsg.ToDate = FilterTo.Value.ToUnixTimeSeconds();
                    AddLog($"Filtering to: {FilterTo.Value:yyyy-MM-dd HH:mm}");
                }

                var req = new Envelope
                {
                    AuthToken = auth.Token ?? "",
                    CorrelationId = Guid.NewGuid().ToString(),
                    MessageType = MessageTypeCode.ListAuditLogsRequest,
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(reqMsg))
                };

                var resp = await broker.SendAsync(req);
                if (resp.StatusCode == 0)
                {
                    var listResp = ProtoHelper.Deserialize<ListAuditLogsResponse>(resp.Payload.ToByteArray());
                    foreach (var l in listResp.Items)
                    {
                        Logs.Add(new AuditLogItem
                        {
                            Id = Guid.Parse(l.Id),
                            Username = l.Username,
                            Action = l.Action,
                            EntityType = l.EntityType,
                            EntityId = l.EntityId ?? "",
                            Details = l.Details ?? "",
                            Timestamp = DateTimeOffset.FromUnixTimeSeconds(l.Timestamp)
                        });
                    }
                    AddLog($"Loaded {listResp.Items.Count} log entr{(listResp.Items.Count == 1 ? "y" : "ies")} from broker.");
                }
                else
                {
                    AddLog($"Broker returned error {resp.StatusCode}: {resp.ErrorMessage}");
                }
            }

            StatusText = $"{Logs.Count} log entr{(Logs.Count == 1 ? "y" : "ies")}";
            AddLog($"Displaying {Logs.Count} log entr{(Logs.Count == 1 ? "y" : "ies")}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load audit logs");
            AddLog($"ERROR loading audit logs: {ex.GetType().Name}: {ex.Message}");
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ExportCsvAsync()
    {
        AddLog($"Exporting {Logs.Count} log entr{(Logs.Count == 1 ? "y" : "ies")} to CSV...");
        var lines = new List<string> { "Timestamp,Username,Action,EntityType,EntityId,Details" };
        foreach (var l in Logs)
        {
            lines.Add($"{l.Timestamp:O},{EscapeCsv(l.Username)},{EscapeCsv(l.Action)},{EscapeCsv(l.EntityType)},{EscapeCsv(l.EntityId)},{EscapeCsv(l.Details)}");
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var path = Path.Combine(desktop, $"audit_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        await File.WriteAllLinesAsync(path, lines);
        StatusText = $"Exported to {path}";
        AddLog($"Exported {Logs.Count} entr{(Logs.Count == 1 ? "y" : "ies")} to {path}");
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

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
