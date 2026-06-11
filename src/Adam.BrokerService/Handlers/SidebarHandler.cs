using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

public sealed class SidebarHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SidebarHandler> _logger;
    private readonly AuthorizationMiddleware _authz;

    public SidebarHandler(IServiceProvider serviceProvider, ILogger<SidebarHandler> logger, AuthorizationMiddleware authz)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _authz = authz;
    }

    public async Task<Envelope> ListFoldersAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, 7, "Forbidden");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var assets = await db.DigitalAssets
            .AsNoTracking()
            .Select(a => a.StoragePath)
            .ToListAsync(ct);

        var root = new FolderNode(string.Empty, 0);
        foreach (var path in assets)
        {
            var dir = GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir))
                continue;

            var parts = dir.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            current.AssetCount++;
            foreach (var part in parts)
            {
                if (!current.Children.TryGetValue(part, out var child))
                {
                    child = new FolderNode(part, 0);
                    current.Children[part] = child;
                }
                child.AssetCount++;
                current = child;
            }
        }

        var response = new ListFoldersResponse();
        AddFoldersToResponse(root.Children, string.Empty, response.Folders);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.ListFoldersResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response))
        };
    }

    public async Task<Envelope> ListKeywordsAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, 7, "Forbidden");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var keywords = await db.Keywords
            .AsNoTracking()
            .Include(k => k.Assets)
            .ToListAsync(ct);

        var response = new ListKeywordsResponse();
        var keywordMap = keywords.ToDictionary(k => k.Id, k => new KeywordInfo
        {
            Id = k.Id,
            Name = k.Name,
            ParentId = k.ParentId,
            AssetCount = k.Assets.Count
        });

        // Propagate counts upward
        foreach (var k in keywordMap.Values.Where(x => x.AssetCount > 0).ToList())
        {
            var currentId = k.ParentId;
            while (currentId.HasValue && keywordMap.TryGetValue(currentId.Value, out var parent))
            {
                parent.AssetCount += k.AssetCount;
                currentId = parent.ParentId;
            }
        }

        response.Keywords.AddRange(keywordMap.Values.OrderBy(k => k.Name));

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.ListKeywordsResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response))
        };
    }

    public async Task<Envelope> ListMediaFormatCountsAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, 7, "Forbidden");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var counts = await db.DigitalAssets
            .AsNoTracking()
            .GroupBy(a => a.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var response = new ListMediaFormatCountsResponse
        {
            TotalCount = counts.Sum(c => c.Count),
            ImageCount = counts.FirstOrDefault(c => c.Type == AssetType.Image)?.Count ?? 0,
            VideoCount = counts.FirstOrDefault(c => c.Type == AssetType.Video)?.Count ?? 0,
            DocumentCount = counts.FirstOrDefault(c => c.Type == AssetType.Document)?.Count ?? 0,
            AudioCount = counts.FirstOrDefault(c => c.Type == AssetType.Audio)?.Count ?? 0
        };

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.ListMediaFormatCountsResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response))
        };
    }

    public async Task<Envelope> ListMetadataCategoriesAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, 7, "Forbidden");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var categories = await db.Categories
            .AsNoTracking()
            .Include(c => c.Assets)
            .ToListAsync(ct);

        var response = new ListMetadataCategoriesResponse();
        var categoryMap = categories.ToDictionary(c => c.Id, c => new CategoryInfo
        {
            Id = c.Id,
            Name = c.Name,
            ParentId = c.ParentId,
            AssetCount = c.Assets.Count
        });

        // Propagate counts upward
        foreach (var c in categoryMap.Values.Where(x => x.AssetCount > 0).ToList())
        {
            var currentId = c.ParentId;
            while (currentId.HasValue && categoryMap.TryGetValue(currentId.Value, out var parent))
            {
                parent.AssetCount += c.AssetCount;
                currentId = parent.ParentId;
            }
        }

        response.Categories.AddRange(categoryMap.Values.OrderBy(c => c.Name));

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.ListMetadataCategoriesResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response))
        };
    }

    public async Task<Envelope> ListDateTakenTreeAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, 7, "Forbidden");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var dateTakenData = await db.MetadataProfiles
            .AsNoTracking()
            .Where(m => m.DateTaken != null)
            .Select(m => new { m.DateTaken!.Value.Year, m.DateTaken.Value.Month })
            .ToListAsync(ct);

        var response = new ListDateTakenTreeResponse();
        foreach (var yearGroup in dateTakenData.GroupBy(d => d.Year).OrderByDescending(y => y.Key))
        {
            var yearInfo = new DateTakenYearInfo
            {
                Year = yearGroup.Key,
                AssetCount = yearGroup.Count()
            };
            foreach (var monthGroup in yearGroup.GroupBy(m => m.Month).OrderBy(m => m.Key))
            {
                yearInfo.Months.Add(new DateTakenMonthInfo
                {
                    Month = monthGroup.Key,
                    MonthName = new DateTime(yearGroup.Key, monthGroup.Key, 1).ToString("MMMM"),
                    AssetCount = monthGroup.Count()
                });
            }
            response.Years.Add(yearInfo);
        }

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.ListDateTakenTreeResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response))
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  Keyword CRUD
    // ─────────────────────────────────────────────────────────────

    public async Task<Envelope> CreateKeywordAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:update", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var req = ProtoHelper.Deserialize<CreateKeywordRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var keyword = new Keyword
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            NormalizedName = req.Name.ToUpperInvariant(),
            ParentId = string.IsNullOrEmpty(req.ParentId) ? null : Guid.Parse(req.ParentId)
        };

        db.Keywords.Add(keyword);
        await db.SaveChangesAsync(ct);

        var response = new CreateKeywordResponse { Id = keyword.Id.ToString() };

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.CreateKeywordResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }

    public async Task<Envelope> UpdateKeywordAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:update", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var req = ProtoHelper.Deserialize<UpdateKeywordRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var keyword = await db.Keywords.FirstOrDefaultAsync(k => k.Id == Guid.Parse(req.Id), ct);
        if (keyword == null)
            return ErrorResponse(request, 5, "Keyword not found");

        keyword.Name = req.Name;
        keyword.NormalizedName = req.Name.ToUpperInvariant();
        await db.SaveChangesAsync(ct);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.UpdateKeywordRequest,
            StatusCode = 0
        };
    }

    public async Task<Envelope> DeleteKeywordAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:update", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var req = ProtoHelper.Deserialize<DeleteKeywordRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var keyword = await db.Keywords.FirstOrDefaultAsync(k => k.Id == Guid.Parse(req.Id), ct);
        if (keyword == null)
            return ErrorResponse(request, 5, "Keyword not found");

        db.Keywords.Remove(keyword);
        await db.SaveChangesAsync(ct);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.DeleteKeywordResponse,
            StatusCode = 0
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  Category CRUD
    // ─────────────────────────────────────────────────────────────

    public async Task<Envelope> CreateCategoryAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:update", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var req = ProtoHelper.Deserialize<CreateCategoryRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            NormalizedName = req.Name.ToUpperInvariant(),
            ParentId = string.IsNullOrEmpty(req.ParentId) ? null : Guid.Parse(req.ParentId)
        };

        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);

        var response = new CreateCategoryResponse { Id = category.Id.ToString() };

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.CreateCategoryResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }

    public async Task<Envelope> UpdateCategoryAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:update", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var req = ProtoHelper.Deserialize<UpdateCategoryRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == Guid.Parse(req.Id), ct);
        if (category == null)
            return ErrorResponse(request, 5, "Category not found");

        category.Name = req.Name;
        category.NormalizedName = req.Name.ToUpperInvariant();
        await db.SaveChangesAsync(ct);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.UpdateCategoryRequest,
            StatusCode = 0
        };
    }

    public async Task<Envelope> DeleteCategoryAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:update", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var req = ProtoHelper.Deserialize<DeleteCategoryRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == Guid.Parse(req.Id), ct);
        if (category == null)
            return ErrorResponse(request, 5, "Category not found");

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.DeleteCategoryResponse,
            StatusCode = 0
        };
    }

    // ─── Helpers ───

    private static string GetDirectoryName(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        var lastSep = path.LastIndexOfAny(['/', '\\']);
        return lastSep > 0 ? path[..lastSep] : string.Empty;
    }

    private static void AddFoldersToResponse(Dictionary<string, FolderNode> children, string parentPath, List<FolderInfo> result)
    {
        foreach (var child in children.Values.OrderBy(c => c.Name))
        {
            var fullPath = string.IsNullOrEmpty(parentPath) ? child.Name : $"{parentPath}/{child.Name}";
            result.Add(new FolderInfo { Path = fullPath, AssetCount = child.AssetCount });
            AddFoldersToResponse(child.Children, fullPath, result);
        }
    }

    private static Envelope ErrorResponse(Envelope request, int statusCode, string message)
    {
        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = request.MessageType,
            StatusCode = statusCode,
            ErrorMessage = message
        };
    }

    private sealed class FolderNode
    {
        public string Name { get; }
        public int AssetCount { get; set; }
        public Dictionary<string, FolderNode> Children { get; } = new();

        public FolderNode(string name, int assetCount)
        {
            Name = name;
            AssetCount = assetCount;
        }
    }
}
