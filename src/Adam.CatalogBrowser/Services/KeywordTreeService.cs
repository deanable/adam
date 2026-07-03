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
/// Manages sidebar keyword tree — loading, creating, renaming, and deleting with cascade.
/// </summary>
public sealed class KeywordTreeService
{
    private readonly ModeManager _modeManager;
    private readonly ILogger<KeywordTreeService> _logger;

    public KeywordTreeService(ModeManager modeManager, ILogger<KeywordTreeService> logger)
    {
        _modeManager = modeManager;
        _logger = logger;
    }

    public ObservableCollection<KeywordNode> Roots { get; } = [];

    public async Task LoadAsync(CancellationToken ct = default)
    {
        KeywordNode root;

        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
            var keywordRows = await db.Keywords
                .Select(k => new
                {
                    k.Id,
                    k.Name,
                    k.NormalizedName,
                    k.ParentId,
                    AssetCount = k.Assets.Count
                })
                .ToListAsync(ct).ConfigureAwait(false);

            root = new KeywordNode { Name = "All Keywords", Path = "", IsExpanded = true };
            var nodeDict = new Dictionary<Guid, KeywordNode>();

            foreach (var kw in keywordRows)
            {
                var node = new KeywordNode
                {
                    Name = kw.Name,
                    Path = kw.Name,
                    KeywordId = kw.Id,
                    AssetCount = kw.AssetCount
                };
                nodeDict[kw.Id] = node;
            }

            foreach (var kw in keywordRows.Where(k => k.ParentId.HasValue))
            {
                if (nodeDict.TryGetValue(kw.Id, out var childNode) &&
                    nodeDict.TryGetValue(kw.ParentId!.Value, out var parentNode))
                {
                    childNode.Path = $"{parentNode.Path}|{childNode.Name}";
                    parentNode.Children.Add(childNode);
                }
            }

            foreach (var kw in keywordRows.Where(k => !k.ParentId.HasValue))
            {
                if (nodeDict.TryGetValue(kw.Id, out var node))
                    root.Children.Add(node);
            }

            root.PropagateCounts();
        }
        else if (_modeManager.BrokerClient != null)
        {
            var req = new Envelope
            {
                MessageType = MessageTypeCode.ListKeywordsRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListKeywordsRequest()))
            };
            var resp = await _modeManager.BrokerClient.SendAsync(req, ct).ConfigureAwait(false);
            root = new KeywordNode { Name = "All Keywords", Path = "", IsExpanded = true };
            if (resp.StatusCode == 0)
            {
                var data = ProtoHelper.Deserialize<ListKeywordsResponse>(resp.Payload.ToByteArray());
                var nodeDict = new Dictionary<Guid, KeywordNode>();

                foreach (var kw in data.Keywords)
                {
                    var node = new KeywordNode
                    {
                        Name = kw.Name,
                        Path = kw.Name,
                        KeywordId = kw.Id,
                        AssetCount = kw.AssetCount
                    };
                    nodeDict[kw.Id] = node;
                }

                foreach (var kw in data.Keywords.Where(k => k.ParentId.HasValue))
                {
                    if (nodeDict.TryGetValue(kw.Id, out var childNode) &&
                        nodeDict.TryGetValue(kw.ParentId!.Value, out var parentNode))
                    {
                        childNode.Path = $"{parentNode.Path}|{childNode.Name}";
                        parentNode.Children.Add(childNode);
                    }
                }

                foreach (var kw in data.Keywords.Where(k => !k.ParentId.HasValue))
                {
                    if (nodeDict.TryGetValue(kw.Id, out var node))
                        root.Children.Add(node);
                }

                root.PropagateCounts();
                _logger.LogInformation("[KeywordTreeService] Loaded {Count} keywords from broker", data.Keywords.Count);
            }
        }
        else
        {
            root = new KeywordNode { Name = "All Keywords", Path = "", IsExpanded = true };
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Roots.Clear();
            Roots.Add(root);
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
                MessageType = MessageTypeCode.CreateKeywordRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(
                    new CreateKeywordRequest { Name = name.Trim(), ParentId = parentId?.ToString() ?? "" }))
            };
            var resp = await _modeManager.BrokerClient!.SendAsync(envelope);
            if (resp.StatusCode != 0)
            {
                _logger.LogWarning("Broker rejected create keyword: status={StatusCode}", resp.StatusCode);
            }
            return;
        }

        await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
        db.Keywords.Add(new Keyword
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            NormalizedName = name.Trim().ToUpperInvariant(),
            ParentId = parentId
        });
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task RenameAsync(KeywordNode node, string newName)
    {
        if (_modeManager.IsMultiUser)
        {
            var envelope = new Envelope
            {
                AuthToken = _modeManager.AuthSession?.Token ?? string.Empty,
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.UpdateKeywordRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(
                    new UpdateKeywordRequest { Id = node.KeywordId.ToString(), Name = newName.Trim() }))
            };
            var resp = await _modeManager.BrokerClient!.SendAsync(envelope);
            if (resp.StatusCode != 0)
            {
                _logger.LogWarning("Broker rejected rename keyword: status={StatusCode}", resp.StatusCode);
            }
            return;
        }

        await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
        var kw = await db.Keywords.FirstOrDefaultAsync(k => k.Id == node.KeywordId).ConfigureAwait(false);
        if (kw == null) return;
        kw.Name = newName.Trim();
        kw.NormalizedName = newName.Trim().ToUpperInvariant();
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a keyword and all its descendants. Returns the list of all IDs deleted.
    /// </summary>
    public async Task<List<Guid>> DeleteWithCascadeAsync(KeywordNode node)
    {
        var allIds = new List<Guid> { node.KeywordId };
        CollectDescendantIds(node, allIds);

        if (_modeManager.IsMultiUser)
        {
            var envelope = new Envelope
            {
                AuthToken = _modeManager.AuthSession?.Token ?? string.Empty,
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.DeleteKeywordRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(
                    new DeleteKeywordRequest { Id = node.KeywordId.ToString(), CascadeChildren = true }))
            };
            var resp = await _modeManager.BrokerClient!.SendAsync(envelope);
            if (resp.StatusCode != 0)
            {
                _logger.LogWarning("Broker rejected delete keyword: status={StatusCode}", resp.StatusCode);
            }
            return allIds;
        }

        await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
        var keywords = await db.Keywords
            .Where(k => allIds.Contains(k.Id))
            .ToListAsync().ConfigureAwait(false);
        db.Keywords.RemoveRange(keywords);
        await db.SaveChangesAsync().ConfigureAwait(false);
        return allIds;
    }

    private static void CollectDescendantIds(KeywordNode node, List<Guid> ids)
    {
        foreach (var child in node.Children)
        {
            ids.Add(child.KeywordId);
            CollectDescendantIds(child, ids);
        }
    }
}
