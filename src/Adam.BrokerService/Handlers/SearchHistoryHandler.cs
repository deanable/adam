using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

public sealed class SearchHistoryHandler : HandlerBase
{
    private readonly AuthHandler _authHandler;

    public SearchHistoryHandler(
        IServiceProvider serviceProvider,
        ILogger<SearchHistoryHandler> logger,
        AuthorizationMiddleware authz,
        AuthHandler authHandler)
        : base(serviceProvider, logger, authz)
    {
        _authHandler = authHandler;
    }

    public async Task<Envelope> RecordSearchHistoryAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var error = DeserializePayload<RecordSearchHistoryRequest>(request, out var req);
        if (error != null) return error;

        if (string.IsNullOrWhiteSpace(req.QueryText))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Query text cannot be empty");

        var userId = _authHandler.GetUserId(request);
        Guid? userGuid = Guid.TryParse(userId, out var uid) ? uid : null;

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entry = new SearchHistoryEntry
        {
            Id = Guid.NewGuid(),
            QueryText = req.QueryText.Trim(),
            FiltersJson = req.FiltersJson,
            IsSemantic = req.IsSemantic,
            ExecutedAt = DateTimeOffset.UtcNow,
            UserId = userGuid
        };

        db.SearchHistoryEntries.Add(entry);

        // Auto-purge: keep only the last 200 entries for this user
        await PurgeOldEntriesAsync(db, userGuid, ct);

        await db.SaveChangesAsync(ct);

        var response = new RecordSearchHistoryResponse
        {
            Id = entry.Id.ToString()
        };

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.RecordSearchHistoryResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = ErrorCode.Success
        };
    }

    public async Task<Envelope> ListSearchHistoryAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        // Parse optional maxResults from payload
        int maxResults = 200;
        if (request.Payload != null && request.Payload.Length > 0)
        {
            try
            {
                var req = ProtoHelper.Deserialize<ListSearchHistoryRequest>(request.Payload.ToByteArray());
                maxResults = req.MaxResults;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to deserialize ListSearchHistoryRequest payload — using defaults");
            }
        }

        var userId = _authHandler.GetUserId(request);
        Guid? userGuid = Guid.TryParse(userId, out var uid) ? uid : null;

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Load to memory first, then sort — SQLite cannot ORDER BY DateTimeOffset
        var all = await db.SearchHistoryEntries
            .Where(s => s.UserId == userGuid)
            .AsNoTracking()
            .ToListAsync(ct);
        List<SearchHistoryEntry> items = [.. all.OrderByDescending(s => s.ExecutedAt).Take(maxResults)];

        var response = new ListSearchHistoryResponse();
        foreach (var item in items)
        {
            response.Items.Add(new SearchHistoryWire
            {
                Id = item.Id.ToString(),
                QueryText = item.QueryText,
                FiltersJson = item.FiltersJson,
                IsSemantic = item.IsSemantic,
                ExecutedAt = item.ExecutedAt.ToUnixTimeSeconds()
            });
        }

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.ListSearchHistoryResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = ErrorCode.Success
        };
    }

    public async Task<Envelope> ClearSearchHistoryAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var userId = _authHandler.GetUserId(request);
        Guid? userGuid = Guid.TryParse(userId, out var uid) ? uid : null;

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entries = await db.SearchHistoryEntries
            .Where(s => s.UserId == userGuid)
            .ToListAsync(ct);

        db.SearchHistoryEntries.RemoveRange(entries);
        await db.SaveChangesAsync(ct);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.ClearSearchHistoryResponse,
            StatusCode = ErrorCode.Success
        };
    }

    private static async Task PurgeOldEntriesAsync(AppDbContext db, Guid? userId, CancellationToken ct)
    {
        if (userId == null) return;

        // Single query: skip the 200 most recent entries, delete the rest
        // Load to memory first, then sort — SQLite cannot ORDER BY DateTimeOffset
        var all = await db.SearchHistoryEntries
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);
        List<SearchHistoryEntry> toDelete = [.. all.OrderByDescending(s => s.ExecutedAt).Skip(200)];

        if (toDelete.Count > 0)
            db.SearchHistoryEntries.RemoveRange(toDelete);
    }


}
