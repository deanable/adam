using System.Collections.ObjectModel;
using Adam.CatalogBrowser.Models.Sidebar;
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
/// Manages sidebar metadata category tree — loading, creating, renaming, and deleting with cascade.
/// </summary>
public sealed class CategoryTreeService
{
    private readonly ModeManager _modeManager;
    private readonly ILogger<CategoryTreeService> _logger;

    public CategoryTreeService(ModeManager modeManager, ILogger<CategoryTreeService> logger)
    {
        _modeManager = modeManager;
        _logger = logger;
    }

    public ObservableCollection<CategoryNode> Roots { get; } = [];

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var newCats = new List<CategoryNode>();

        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
            var categoryRows = await db.Categories
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.NormalizedName,
                    c.ParentId,
                    AssetCount = c.Assets.Count
                })
                .ToListAsync(ct).ConfigureAwait(false);

            var total = categoryRows.Sum(c => c.AssetCount);
            var root = new CategoryNode { Name = "All", Count = total, IsExpanded = true };
            var nodeDict = new Dictionary<Guid, CategoryNode>();

            foreach (var cat in categoryRows)
            {
                var node = new CategoryNode
                {
                    Name = cat.Name,
                    CategoryId = cat.Id,
                    Count = cat.AssetCount
                };
                nodeDict[cat.Id] = node;
            }

            foreach (var cat in categoryRows.Where(c => c.ParentId.HasValue))
            {
                if (nodeDict.TryGetValue(cat.Id, out var childNode) &&
                    nodeDict.TryGetValue(cat.ParentId!.Value, out var parentNode))
                {
                    parentNode.Children.Add(childNode);
                }
            }

            foreach (var cat in categoryRows.Where(c => !c.ParentId.HasValue))
            {
                if (nodeDict.TryGetValue(cat.Id, out var node))
                    root.Children.Add(node);
            }

            root.PropagateCounts();
            newCats.Add(root);
        }
        else if (_modeManager.BrokerClient != null)
        {
            var req = new Envelope
            {
                MessageType = MessageTypeCode.ListMetadataCategoriesRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListMetadataCategoriesRequest()))
            };
            var resp = await _modeManager.BrokerClient.SendAsync(req, ct).ConfigureAwait(false);
            if (resp.StatusCode == 0)
            {
                var data = ProtoHelper.Deserialize<ListMetadataCategoriesResponse>(resp.Payload.ToByteArray());
                var total = data.Categories.Sum(c => c.AssetCount);
                var root = new CategoryNode { Name = "All", Count = total, IsExpanded = true };
                var nodeDict = new Dictionary<Guid, CategoryNode>();

                foreach (var cat in data.Categories)
                {
                    var node = new CategoryNode
                    {
                        Name = cat.Name,
                        CategoryId = cat.Id,
                        Count = cat.AssetCount
                    };
                    nodeDict[cat.Id] = node;
                }

                foreach (var cat in data.Categories.Where(c => c.ParentId.HasValue))
                {
                    if (nodeDict.TryGetValue(cat.Id, out var childNode) &&
                        nodeDict.TryGetValue(cat.ParentId!.Value, out var parentNode))
                    {
                        parentNode.Children.Add(childNode);
                    }
                }

                foreach (var cat in data.Categories.Where(c => !c.ParentId.HasValue))
                {
                    if (nodeDict.TryGetValue(cat.Id, out var node))
                        root.Children.Add(node);
                }

                root.PropagateCounts();
                newCats.Add(root);
                _logger.LogInformation("[CategoryTreeService] Loaded {Count} categories from broker", data.Categories.Count);
            }
            else
            {
                newCats.Add(new CategoryNode { Name = "All", Count = 0, IsExpanded = true });
            }
        }
        else
        {
            newCats.Add(new CategoryNode { Name = "All", Count = 0, IsExpanded = true });
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Roots.Clear();
            foreach (var c in newCats)
                Roots.Add(c);
        });
    }

    public async Task CreateAsync(string name, Guid? parentId)
    {
        if (_modeManager.IsMultiUser)
        {
            var envelope = new Envelope
            {
                AuthToken = _modeManager.AuthSession?.Token ?? string.Empty,
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.CreateCategoryRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(
                    new CreateCategoryRequest { Name = name.Trim(), ParentId = parentId?.ToString() ?? "" }))
            };
            var resp = await _modeManager.BrokerClient!.SendAsync(envelope);
            if (resp.StatusCode != 0)
            {
                _logger.LogWarning("Broker rejected create category: status={StatusCode}", resp.StatusCode);
            }
            return;
        }

        await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
        db.Categories.Add(new Category
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            NormalizedName = name.Trim().ToUpperInvariant(),
            ParentId = parentId
        });
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task RenameAsync(CategoryNode node, string newName)
    {
        if (_modeManager.IsMultiUser)
        {
            var envelope = new Envelope
            {
                AuthToken = _modeManager.AuthSession?.Token ?? string.Empty,
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.UpdateCategoryRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(
                    new UpdateCategoryRequest { Id = node.CategoryId.ToString(), Name = newName.Trim() }))
            };
            var resp = await _modeManager.BrokerClient!.SendAsync(envelope);
            if (resp.StatusCode != 0)
            {
                _logger.LogWarning("Broker rejected rename category: status={StatusCode}", resp.StatusCode);
            }
            return;
        }

        await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
        var cat = await db.Categories.FirstOrDefaultAsync(c => c.Id == node.CategoryId).ConfigureAwait(false);
        if (cat == null) return;
        cat.Name = newName.Trim();
        cat.NormalizedName = newName.Trim().ToUpperInvariant();
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a category and all its descendants. Returns the list of all IDs deleted.
    /// </summary>
    public async Task<List<Guid>> DeleteWithCascadeAsync(CategoryNode node)
    {
        var allIds = new List<Guid> { node.CategoryId };
        CollectDescendantIds(node, allIds);

        if (_modeManager.IsMultiUser)
        {
            var envelope = new Envelope
            {
                AuthToken = _modeManager.AuthSession?.Token ?? string.Empty,
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.DeleteCategoryRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(
                    new DeleteCategoryRequest { Id = node.CategoryId.ToString(), CascadeChildren = true }))
            };
            var resp = await _modeManager.BrokerClient!.SendAsync(envelope);
            if (resp.StatusCode != 0)
            {
                _logger.LogWarning("Broker rejected delete category: status={StatusCode}", resp.StatusCode);
            }
            return allIds;
        }

        await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
        var cats = await db.Categories
            .Where(c => allIds.Contains(c.Id))
            .ToListAsync().ConfigureAwait(false);
        db.Categories.RemoveRange(cats);
        await db.SaveChangesAsync().ConfigureAwait(false);
        return allIds;
    }

    private static void CollectDescendantIds(CategoryNode node, List<Guid> ids)
    {
        foreach (var child in node.Children)
        {
            ids.Add(child.CategoryId);
            CollectDescendantIds(child, ids);
        }
    }
}
