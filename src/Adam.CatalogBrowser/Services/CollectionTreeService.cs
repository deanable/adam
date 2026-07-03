using System.Collections.ObjectModel;
using Adam.CatalogBrowser.Models.Sidebar;
// Resolves ambiguity with Adam.Shared.Contracts.CollectionNode (protobuf message)
using CollectionNode = Adam.CatalogBrowser.Models.Sidebar.CollectionNode;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Avalonia.Threading;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.Services;

/// <summary>
/// Manages sidebar collection tree — loading, creating, renaming, and deleting with cascade.
/// </summary>
public sealed class CollectionTreeService
{
    private readonly ModeManager _modeManager;
    private readonly ILogger<CollectionTreeService> _logger;

    public CollectionTreeService(ModeManager modeManager, ILogger<CollectionTreeService> logger)
    {
        _modeManager = modeManager;
        _logger = logger;
    }

    public ObservableCollection<CollectionNode> Roots { get; } = [];

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var newCollections = new List<CollectionNode>();

        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
            var all = await db.Collections
                .Select(c => new { c.Id, c.IsSmart, c.Name, c.ParentId, AssetCount = c.Assets.Count })
                .ToListAsync(ct).ConfigureAwait(false);
            var allCols = all.Select(c => new CollectionNode
            {
                Id = c.Id, Name = c.Name, ParentId = c.ParentId, AssetCount = c.AssetCount, IsSmart = c.IsSmart
            }).ToList();

            foreach (var col in allCols.Where(c => c.ParentId == null))
                newCollections.Add(BuildTree(col, allCols));
        }
        else if (_modeManager.BrokerClient != null)
        {
            var req = new Envelope
            {
                MessageType = MessageTypeCode.ListCollectionsRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListCollectionsRequest()))
            };
            var resp = await _modeManager.BrokerClient.SendAsync(req, ct).ConfigureAwait(false);
            if (resp.StatusCode == 0)
            {
                var data = ProtoHelper.Deserialize<ListCollectionsResponse>(resp.Payload.ToByteArray());
                var allCols = data.Items.Select(c => new CollectionNode
                {
                    Id = Guid.Parse(c.Id),
                    Name = c.Name,
                    ParentId = string.IsNullOrEmpty(c.ParentId) ? null : Guid.Parse(c.ParentId),
                    AssetCount = c.AssetCount,
                    IsSmart = c.IsSmart
                }).ToList();

                foreach (var col in allCols.Where(c => c.ParentId == null))
                    newCollections.Add(BuildTree(col, allCols));

                _logger.LogInformation("[CollectionTreeService] Loaded {Count} collections from broker", allCols.Count);
            }
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Roots.Clear();
            foreach (var c in newCollections)
                Roots.Add(c);
        });
    }

    internal static CollectionNode BuildTree(CollectionNode node, List<CollectionNode> all)
    {
        foreach (var child in all.Where(c => c.ParentId == node.Id))
            node.Children.Add(BuildTree(child, all));
        return node;
    }

    public async Task CreateAsync(string name, Guid? parentId)
    {
        if (_modeManager.IsMultiUser)
        {
            var envelope = new Envelope
            {
                AuthToken = _modeManager.AuthSession?.Token ?? string.Empty,
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.CreateCollectionRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(
                    new CreateCollectionRequest { Name = name.Trim(), ParentId = parentId?.ToString() ?? "" }))
            };
            var resp = await _modeManager.BrokerClient!.SendAsync(envelope);
            if (resp.StatusCode != 0)
            {
                _logger.LogWarning("Broker rejected create collection: status={StatusCode}", resp.StatusCode);
            }
            return;
        }

        await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
        db.Collections.Add(new Collection
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            ParentId = parentId
        });
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task RenameAsync(CollectionNode node, string newName)
    {
        if (_modeManager.IsMultiUser)
        {
            var envelope = new Envelope
            {
                AuthToken = _modeManager.AuthSession?.Token ?? string.Empty,
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.UpdateCollectionRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(
                    new UpdateCollectionRequest { Id = node.Id.ToString(), Name = newName.Trim() }))
            };
            var resp = await _modeManager.BrokerClient!.SendAsync(envelope);
            if (resp.StatusCode != 0)
            {
                _logger.LogWarning("Broker rejected rename collection: status={StatusCode}", resp.StatusCode);
            }
            return;
        }

        await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
        var col = await db.Collections.FirstOrDefaultAsync(c => c.Id == node.Id).ConfigureAwait(false);
        if (col == null) return;
        col.Name = newName.Trim();
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a collection and all its descendants, with cascade confirmation support.
    /// Returns the list of all IDs that were deleted.
    /// </summary>
    public async Task<List<Guid>> DeleteWithCascadeAsync(CollectionNode node)
    {
        var allIds = new List<Guid> { node.Id };
        CollectDescendantIds(node, allIds);

        if (_modeManager.IsMultiUser)
        {
            var envelope = new Envelope
            {
                AuthToken = _modeManager.AuthSession?.Token ?? string.Empty,
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.DeleteCollectionRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(
                    new DeleteCollectionRequest { Id = node.Id.ToString(), CascadeChildren = true }))
            };
            var resp = await _modeManager.BrokerClient!.SendAsync(envelope);
            if (resp.StatusCode != 0)
            {
                _logger.LogWarning("Broker rejected delete collection: status={StatusCode}", resp.StatusCode);
            }
            return allIds;
        }

        await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
        var cols = await db.Collections
            .Where(c => allIds.Contains(c.Id))
            .ToListAsync().ConfigureAwait(false);
        db.Collections.RemoveRange(cols);
        await db.SaveChangesAsync().ConfigureAwait(false);
        return allIds;
    }

    private static void CollectDescendantIds(CollectionNode node, List<Guid> ids)
    {
        foreach (var child in node.Children)
        {
            ids.Add(child.Id);
            CollectDescendantIds(child, ids);
        }
    }
}
