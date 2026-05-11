using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Contracts;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;

namespace Adam.CatalogBrowser.ViewModels;

public class AuditLogViewModel : INotifyPropertyChanged
{
    private readonly ModeManager _modeManager;
    private string _statusText = string.Empty;
    private bool _isLoading;
    private string _filterAction = string.Empty;
    private string _filterEntityType = string.Empty;
    private DateTimeOffset? _filterFrom;
    private DateTimeOffset? _filterTo;

    public AuditLogViewModel(ModeManager modeManager)
    {
        _modeManager = modeManager;
        RefreshCommand = new RelayCommand(async _ => await LoadLogsAsync());
        ExportCsvCommand = new RelayCommand(async _ => await ExportCsvAsync());
    }

    public ObservableCollection<AuditLogItem> Logs { get; } = [];

    public ICommand RefreshCommand { get; }
    public ICommand ExportCsvCommand { get; }

    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }
    public string FilterAction { get => _filterAction; set { _filterAction = value; OnPropertyChanged(); } }
    public string FilterEntityType { get => _filterEntityType; set { _filterEntityType = value; OnPropertyChanged(); } }
    public DateTimeOffset? FilterFrom { get => _filterFrom; set { _filterFrom = value; OnPropertyChanged(); } }
    public DateTimeOffset? FilterTo { get => _filterTo; set { _filterTo = value; OnPropertyChanged(); } }

    public async Task LoadLogsAsync()
    {
        IsLoading = true;
        Logs.Clear();

        try
        {
            if (_modeManager.IsStandalone)
            {
                await using var db = _modeManager.CreateDbContext();
                var query = db.AccessLogs.Include(l => l.User).AsQueryable();

                if (!string.IsNullOrEmpty(FilterAction))
                    query = query.Where(l => l.Action == FilterAction);
                if (!string.IsNullOrEmpty(FilterEntityType))
                    query = query.Where(l => l.EntityType == FilterEntityType);
                if (FilterFrom.HasValue)
                    query = query.Where(l => l.Timestamp >= FilterFrom.Value);
                if (FilterTo.HasValue)
                    query = query.Where(l => l.Timestamp <= FilterTo.Value);

                var logs = await query.OrderByDescending(l => l.Timestamp).Take(500).ToListAsync();

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
            }
            else if (_modeManager.IsMultiUser)
            {
                var broker = _modeManager.BrokerClient;
                var auth = _modeManager.AuthSession;
                if (broker == null || auth == null) return;

                if (!broker.IsConnected)
                    await broker.ConnectAsync();

                var reqMsg = new ListAuditLogsRequest();
                if (!string.IsNullOrEmpty(FilterAction))
                    reqMsg.Action = FilterAction;
                if (!string.IsNullOrEmpty(FilterEntityType))
                    reqMsg.EntityType = FilterEntityType;
                if (FilterFrom.HasValue)
                    reqMsg.FromDate = FilterFrom.Value.ToUnixTimeSeconds();
                if (FilterTo.HasValue)
                    reqMsg.ToDate = FilterTo.Value.ToUnixTimeSeconds();

                var req = new Envelope
                {
                    AuthToken = auth.Token ?? "",
                    CorrelationId = Guid.NewGuid().ToString(),
                    MessageType = nameof(ListAuditLogsRequest),
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
                }
            }

            StatusText = $"{Logs.Count} log entries";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ExportCsvAsync()
    {
        var lines = new List<string> { "Timestamp,Username,Action,EntityType,EntityId,Details" };
        foreach (var l in Logs)
        {
            lines.Add($"{l.Timestamp:O},{EscapeCsv(l.Username)},{EscapeCsv(l.Action)},{EscapeCsv(l.EntityType)},{EscapeCsv(l.EntityId)},{EscapeCsv(l.Details)}");
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var path = Path.Combine(desktop, $"audit_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        await File.WriteAllLinesAsync(path, lines);
        StatusText = $"Exported to {path}";
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
