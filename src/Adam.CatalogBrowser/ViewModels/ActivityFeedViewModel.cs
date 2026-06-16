using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Models;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Contracts;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Adam.Shared.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.CatalogBrowser.ViewModels;

/// <summary>
/// ViewModel for the Activity Feed / Recent Changes panel (T14.3).
/// Shows recent catalog changes from ChangeNotification broadcasts (multi-user)
/// or AccessLog queries (standalone).
/// </summary>
public sealed class ActivityFeedViewModel : INotifyPropertyChanged
{
    private readonly ModeManager _modeManager;
    private readonly ILogger<ActivityFeedViewModel> _logger;
    private readonly IUiDispatcher _dispatcher;
    private bool _isLoading;
    private bool _isExpanded = true;
    private string _filterEntityType = string.Empty;
    private const int MaxEntries = 100;

    /// <summary>
    /// Creates the activity feed VM and wires up the ChangeNotification listener on the broker client.
    /// </summary>
    public ActivityFeedViewModel(
        ModeManager modeManager,
        ILogger<ActivityFeedViewModel>? logger = null,
        IUiDispatcher? dispatcher = null)
    {
        _modeManager = modeManager;
        _logger = logger ?? NullLogger<ActivityFeedViewModel>.Instance;
        _dispatcher = dispatcher ?? new AvaloniaUiDispatcher();

        // Wire broker ChangeNotification events for live updates (multi-user mode)
        if (modeManager.BrokerClient != null)
        {
            modeManager.BrokerClient.NotificationReceived += (_, notification) =>
            {
                _dispatcher.Post(() => OnChangeNotification(notification));
            };
        }

        RefreshCommand = new RelayCommand(async _ => await LoadRecentActivityAsync());
        MarkAllAsReadCommand = new RelayCommand(_ => MarkAllAsRead());
        ClearAllCommand = new RelayCommand(_ => ClearAll());
        ClearFilterCommand = new RelayCommand(_ => { FilterEntityType = string.Empty; });

        // Note: mode switch reload is handled externally by MainWindowViewModel
        // (ModeManager does not implement INotifyPropertyChanged)
    }

    /// <summary>Activity entries, most recent first.</summary>
    public ObservableCollection<ActivityEntry> Entries { get; } = [];

    /// <summary>Whether data is currently loading.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    /// <summary>Whether the activity panel is expanded.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    /// <summary>Number of unread entries.</summary>
    public int UnreadCount => Entries.Count(e => !e.IsRead);

    /// <summary>Summary text for the header, e.g. "Activity (3 unread)".</summary>
    public string HeaderText
    {
        get
        {
            var unread = UnreadCount;
            return unread > 0 ? $"Activity ({unread} new)" : "Activity";
        }
    }

    /// <summary>True when there are unread entries (controls badge visibility).</summary>
    public bool HasUnread => UnreadCount > 0;

    /// <summary>True when there are any entries (controls empty state).</summary>
    public bool HasEntries => Entries.Count > 0;

    /// <summary>Filter by entity type (empty string = no filter).</summary>
    public string FilterEntityType
    {
        get => _filterEntityType;
        set
        {
            _filterEntityType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasActiveFilter));
        }
    }

    /// <summary>True when a filter is active.</summary>
    public bool HasActiveFilter => !string.IsNullOrEmpty(FilterEntityType);

    /// <summary>Commands.</summary>
    public ICommand RefreshCommand { get; }
    public ICommand MarkAllAsReadCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand ClearFilterCommand { get; }

    /// <summary>
    /// Loads recent activity from the AccessLog table (standalone mode) or ChangeNotification history.
    /// </summary>
    public async Task LoadRecentActivityAsync(int maxEntries = 50)
    {
        IsLoading = true;

        try
        {
            if (_modeManager.IsStandalone)
            {
                await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);

                var filtered = db.AccessLogs
                    .Include(l => l.User)
                    .Where(l => l.Timestamp > DateTimeOffset.UtcNow.AddDays(-30));

                if (!string.IsNullOrEmpty(FilterEntityType))
                {
                    filtered = filtered.Where(l => l.EntityType == FilterEntityType);
                }

                var query = filtered
                    .OrderByDescending(l => l.Timestamp)
                    .Take(maxEntries);

                var logs = await query.ToListAsync().ConfigureAwait(false);

                // Resolve asset names in a batch query
                var resolved = new Dictionary<Guid, string?>();
                var assetIds = logs
                    .Where(l => (l.EntityType == "Asset" || l.EntityType == "DigitalAsset") && l.EntityId.HasValue)
                    .Select(l => l.EntityId!.Value)
                    .Distinct()
                    .ToList();

                if (assetIds.Count > 0)
                {
                    try
                    {
                        var titles = await db.DigitalAssets
                            .Where(a => assetIds.Contains(a.Id))
                            .Select(a => new { a.Id, a.Title })
                            .ToListAsync()
                            .ConfigureAwait(false);
                        foreach (var t in titles)
                            resolved[t.Id] = t.Title;
                    }
                    catch { }
                }

                await _dispatcher.InvokeAsync(() =>
                {
                    Entries.Clear();
                    foreach (var log in logs)
                    {
                        var entityId = log.EntityId;
                        var assetName = entityId.HasValue && resolved.TryGetValue(entityId.Value, out var name) ? name : null;

                        Entries.Add(new ActivityEntry
                        {
                            Id = log.Id.ToString(),
                            EntityType = log.EntityType,
                            ChangeType = NormalizeAction(log.Action),
                            EntityId = entityId?.ToString() ?? string.Empty,
                            AssetName = assetName,
                            UserName = log.User?.Username ?? "System",
                            Timestamp = log.Timestamp.UtcDateTime,
                            IsRead = false
                        });
                    }
                    OnPropertyChanged(nameof(UnreadCount));
                    OnPropertyChanged(nameof(HeaderText));
                    OnPropertyChanged(nameof(HasUnread));
                    OnPropertyChanged(nameof(HasEntries));
                });
            }
            else if (_modeManager.IsMultiUser)
            {
                // Multi-user mode: load from broker's audit log
                var broker = _modeManager.BrokerClient;
                var auth = _modeManager.AuthSession;
                if (broker == null || auth == null) return;

                if (!broker.IsConnected)
                    await broker.ConnectAsync();

                // Query recent audit logs from the broker
                var reqMsg = new ListAuditLogsRequest
                {
                    FromDate = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds(),
                    ToDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                if (!string.IsNullOrEmpty(FilterEntityType))
                {
                    reqMsg.EntityType = FilterEntityType;
                }

                var req = new Envelope
                {
                    AuthToken = auth.Token ?? string.Empty,
                    CorrelationId = Guid.NewGuid().ToString(),
                    MessageType = MessageTypeCode.ListAuditLogsRequest,
                    Payload = Google.Protobuf.ByteString.CopyFrom(ProtoHelper.Serialize(reqMsg))
                };

                var resp = await broker.SendAsync(req);
                if (resp.StatusCode == 0)
                {
                    var listResp = ProtoHelper.Deserialize<ListAuditLogsResponse>(resp.Payload.ToByteArray());

                    await _dispatcher.InvokeAsync(() =>
                    {
                        Entries.Clear();
                        foreach (var l in listResp.Items.Take(maxEntries))
                        {
                            Entries.Add(new ActivityEntry
                            {
                                Id = l.Id,
                                EntityType = l.EntityType,
                                ChangeType = NormalizeAction(l.Action),
                                EntityId = l.EntityId ?? string.Empty,
                                AssetName = l.EntityType == "Asset" || l.EntityType == "DigitalAsset"
                                    ? ResolveAssetNameFromBroker(l.EntityId)
                                    : null,
                                UserName = l.Username,
                                Timestamp = DateTimeOffset.FromUnixTimeSeconds(l.Timestamp).UtcDateTime,
                                IsRead = false
                            });
                        }
                        OnPropertyChanged(nameof(UnreadCount));
                        OnPropertyChanged(nameof(HeaderText));
                        OnPropertyChanged(nameof(HasUnread));
                        OnPropertyChanged(nameof(HasEntries));
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load recent activity");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Handles an incoming ChangeNotification from the broker (live push).
    /// Prepend to the Entries collection.
    /// </summary>
    public void OnChangeNotification(ChangeNotification notification)
    {
        var ts = DateTimeOffset.FromUnixTimeSeconds(notification.Timestamp);

        var entry = new ActivityEntry
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "Asset", // ChangeNotification always refers to assets
            ChangeType = NormalizeAction(notification.Action),
            EntityId = notification.EntityId,
            AssetName = null, // Not resolved for push notifications (would need extra query)
            UserName = string.IsNullOrEmpty(notification.ChangedByUserId)
                ? "System"
                : $"User:{notification.ChangedByUserId[..Math.Min(8, notification.ChangedByUserId.Length)]}",
            Timestamp = ts.UtcDateTime,
            IsRead = false
        };

        // Prepend to the list (most recent first)
        Entries.Insert(0, entry);

        // Trim to max entries
        while (Entries.Count > MaxEntries)
            Entries.RemoveAt(Entries.Count - 1);

        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(HasEntries));
    }

    /// <summary>Marks all entries as read.</summary>
    public void MarkAllAsRead()
    {
        foreach (var entry in Entries)
            entry.IsRead = true;

        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(HasUnread));
    }

    /// <summary>Clears all entries.</summary>
    public void ClearAll()
    {
        Entries.Clear();
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(HasEntries));
    }

    /// <summary>Normalizes action strings from the database/broker to a consistent set.</summary>
    private static string NormalizeAction(string action)
    {
        return action.ToLowerInvariant() switch
        {
            "create" or "created" or "insert" => "created",
            "update" or "updated" or "modify" or "modified" => "updated",
            "delete" or "deleted" or "remove" or "removed" => "deleted",
            _ => action.ToLowerInvariant()
        };
    }

    /// <summary>Stub for resolving asset names from the broker — returns null for now.</summary>
    private static string? ResolveAssetNameFromBroker(string? entityId)
    {
        // In a full implementation, this would query the broker for the asset title.
        // For now, return null (shows just the entity type in the summary).
        return null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
