using System.Collections.ObjectModel;
using Adam.CatalogBrowser.Models.Sidebar;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Services;
using Avalonia.Threading;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.Services;

/// <summary>
/// Manages saved searches and search history — loading, deleting, pinning, and clearing.
/// </summary>
public sealed class SavedSearchService
{
    private readonly ModeManager _modeManager;
    private readonly ILogger<SavedSearchService> _logger;

    public SavedSearchService(ModeManager modeManager, ILogger<SavedSearchService> logger)
    {
        _modeManager = modeManager;
        _logger = logger;
    }

    public ObservableCollection<SavedSearchNode> SavedSearches { get; } = [];
    public ObservableCollection<SearchHistoryNode> RecentSearches { get; } = [];

    public async Task LoadSavedSearchesAsync(CancellationToken ct = default)
    {
        var items = new List<SavedSearchNode>();

        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
            var saved = await db.SavedSearches
                .OrderByDescending(s => s.IsPinned)
                .ThenBy(s => s.Name)
                .AsNoTracking()
                .ToListAsync(ct).ConfigureAwait(false);

            items = saved.Select(s => new SavedSearchNode
            {
                SearchId = s.Id,
                Name = s.Name,
                QueryText = s.QueryText,
                IsPinned = s.IsPinned
            }).ToList();

            _logger.LogInformation("[SavedSearchService] Loaded {Count} saved searches", items.Count);
        }
        else if (_modeManager.BrokerClient != null)
        {
            var envelope = new Envelope
            {
                AuthToken = _modeManager.AuthSession?.Token ?? string.Empty,
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.ListSavedSearchesRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListSavedSearchesRequest()))
            };
            var resp = await _modeManager.BrokerClient.SendAsync(envelope, ct).ConfigureAwait(false);
            if (resp != null && resp.StatusCode == 0)
            {
                var data = ProtoHelper.Deserialize<ListSavedSearchesResponse>(resp.Payload.ToByteArray());
                items = data.Items.Select(w => new SavedSearchNode
                {
                    SearchId = Guid.Parse(w.Id),
                    Name = w.Name,
                    QueryText = w.QueryText,
                    IsPinned = w.IsPinned
                }).ToList();
                _logger.LogInformation("[SavedSearchService] Loaded {Count} saved searches from broker", items.Count);
            }
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            SavedSearches.Clear();
            foreach (var item in items)
                SavedSearches.Add(item);
        });
    }

    public async Task LoadRecentSearchesAsync(CancellationToken ct = default)
    {
        var items = new List<SearchHistoryNode>();

        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
            var history = (await db.SearchHistoryEntries
                .AsNoTracking()
                .ToListAsync(ct).ConfigureAwait(false))
                .OrderByDescending(h => h.ExecutedAt)
                .Take(200)
                .ToList();

            items = history.Select(h => new SearchHistoryNode
            {
                EntryId = h.Id,
                QueryText = h.QueryText,
                ExecutedAt = h.ExecutedAt
            }).ToList();

            _logger.LogInformation("[SavedSearchService] Loaded {Count} recent searches", items.Count);
        }
        else if (_modeManager.BrokerClient != null)
        {
            var envelope = new Envelope
            {
                AuthToken = _modeManager.AuthSession?.Token ?? string.Empty,
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.ListSearchHistoryRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListSearchHistoryRequest { MaxResults = 200 }))
            };
            var resp = await _modeManager.BrokerClient.SendAsync(envelope, ct).ConfigureAwait(false);
            if (resp != null && resp.StatusCode == 0)
            {
                var data = ProtoHelper.Deserialize<ListSearchHistoryResponse>(resp.Payload.ToByteArray());
                items = data.Items.Select(w => new SearchHistoryNode
                {
                    EntryId = Guid.Parse(w.Id),
                    QueryText = w.QueryText,
                    ExecutedAt = DateTimeOffset.FromUnixTimeSeconds(w.ExecutedAt)
                }).ToList();
                _logger.LogInformation("[SavedSearchService] Loaded {Count} recent searches from broker", items.Count);
            }
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            RecentSearches.Clear();
            foreach (var item in items)
                RecentSearches.Add(item);
        });
    }

    public async Task DeleteSavedSearchAsync(Guid searchId)
    {
        try
        {
            if (_modeManager.IsStandalone)
            {
                await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
                var saved = await db.SavedSearches.FirstOrDefaultAsync(s => s.Id == searchId).ConfigureAwait(false);
                if (saved != null)
                {
                    db.SavedSearches.Remove(saved);
                    await db.SaveChangesAsync().ConfigureAwait(false);
                }
            }
            else if (_modeManager.BrokerClient != null)
            {
                var envelope = new Envelope
                {
                    AuthToken = _modeManager.AuthSession?.Token ?? string.Empty,
                    CorrelationId = Guid.NewGuid().ToString(),
                    MessageType = MessageTypeCode.DeleteSavedSearchRequest,
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new DeleteSavedSearchRequest { Id = searchId.ToString() }))
                };
                await _modeManager.BrokerClient.SendAsync(envelope);
            }

            // Remove from in-memory collection
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var toRemove = SavedSearches.Where(s => s.SearchId == searchId).ToList();
                foreach (var s in toRemove)
                    SavedSearches.Remove(s);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete saved search: {SearchId}", searchId);
        }
    }

    public async Task PersistPinStateAsync(Guid searchId, bool isPinned)
    {
        try
        {
            if (_modeManager.IsStandalone)
            {
                await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
                var saved = await db.SavedSearches.FirstOrDefaultAsync(s => s.Id == searchId).ConfigureAwait(false);
                if (saved != null)
                {
                    saved.IsPinned = isPinned;
                    await db.SaveChangesAsync().ConfigureAwait(false);
                }
            }
            else if (_modeManager.BrokerClient != null)
            {
                var envelope = new Envelope
                {
                    AuthToken = _modeManager.AuthSession?.Token ?? string.Empty,
                    CorrelationId = Guid.NewGuid().ToString(),
                    MessageType = MessageTypeCode.PinSavedSearchRequest,
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new PinSavedSearchRequest
                    {
                        Id = searchId.ToString(),
                        IsPinned = isPinned
                    }))
                };
                await _modeManager.BrokerClient.SendAsync(envelope);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist pin state: {SearchId}", searchId);
        }
    }

    public void ClearRecentSearches()
    {
        foreach (var rs in RecentSearches)
            rs.IsActiveFilter = false;
        RecentSearches.Clear();
    }

    public void ClearSavedSearchActiveStates()
    {
        foreach (var ss in SavedSearches)
            ss.IsActiveFilter = false;
    }

    public void ClearRecentSearchActiveStates()
    {
        foreach (var rs in RecentSearches)
            rs.IsActiveFilter = false;
    }
}
