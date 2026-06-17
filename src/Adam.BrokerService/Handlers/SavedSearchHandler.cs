using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

public sealed class SavedSearchHandler : HandlerBase
{
    private readonly AuthHandler _authHandler;

    public SavedSearchHandler(
        IServiceProvider serviceProvider,
        ILogger<SavedSearchHandler> logger,
        AuthorizationMiddleware authz,
        AuthHandler authHandler)
        : base(serviceProvider, logger, authz)
    {
        _authHandler = authHandler;
    }

    public async Task<Envelope> CreateSavedSearchAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var error = DeserializePayload<CreateSavedSearchRequest>(request, out var req);
        if (error != null) return error;

        if (string.IsNullOrWhiteSpace(req.Name))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Name cannot be empty");

        var userId = _authHandler.GetUserId(request);
        Guid? userGuid = Guid.TryParse(userId, out var uid) ? uid : null;

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Check for duplicate name per user
        var existing = await db.SavedSearches
            .FirstOrDefaultAsync(s => s.UserId == userGuid && s.Name == req.Name, ct);
        if (existing != null)
            return ErrorResponse(request, ErrorCode.Conflict, "A saved search with this name already exists");

        var now = DateTimeOffset.UtcNow;
        var saved = new SavedSearch
        {
            Id = Guid.NewGuid(),
            Name = req.Name.Trim(),
            QueryText = string.IsNullOrWhiteSpace(req.QueryText) ? null : req.QueryText.Trim(),
            FiltersJson = req.FiltersJson,
            IsPinned = req.IsPinned,
            UserId = userGuid,
            CreatedAt = now,
            ModifiedAt = now
        };

        db.SavedSearches.Add(saved);
        await db.SaveChangesAsync(ct);

        var response = new CreateSavedSearchResponse
        {
            Id = saved.Id.ToString(),
            CreatedAt = now.ToUnixTimeSeconds()
        };

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.CreateSavedSearchResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = ErrorCode.Success
        };
    }

    public async Task<Envelope> ListSavedSearchesAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var userId = _authHandler.GetUserId(request);
        Guid? userGuid = Guid.TryParse(userId, out var uid) ? uid : null;

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var items = await db.SavedSearches
            .Where(s => s.UserId == userGuid)
            .OrderByDescending(s => s.IsPinned)
            .ThenBy(s => s.Name)
            .AsNoTracking()
            .ToListAsync(ct);

        var response = new ListSavedSearchesResponse();
        foreach (var item in items)
        {
            response.Items.Add(new SavedSearchWire
            {
                Id = item.Id.ToString(),
                Name = item.Name,
                QueryText = item.QueryText,
                FiltersJson = item.FiltersJson,
                IsPinned = item.IsPinned,
                CreatedAt = item.CreatedAt.ToUnixTimeSeconds(),
                ModifiedAt = item.ModifiedAt.ToUnixTimeSeconds()
            });
        }

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.ListSavedSearchesResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = ErrorCode.Success
        };
    }

    public async Task<Envelope> UpdateSavedSearchAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var updateError = DeserializePayload<UpdateSavedSearchRequest>(request, out var updateReq);
        if (updateError != null) return updateError;

        if (!Guid.TryParse(updateReq.Id, out var id))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid ID");

        if (string.IsNullOrWhiteSpace(updateReq.Name))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Name cannot be empty");

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var saved = await db.SavedSearches.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (saved == null)
            return ErrorResponse(request, ErrorCode.NotFound, "Saved search not found");

        saved.Name = updateReq.Name.Trim();
        saved.QueryText = string.IsNullOrWhiteSpace(updateReq.QueryText) ? null : updateReq.QueryText.Trim();
        saved.FiltersJson = updateReq.FiltersJson;
        saved.ModifiedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        var response = new UpdateSavedSearchResponse
        {
            ModifiedAt = saved.ModifiedAt.ToUnixTimeSeconds()
        };

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.UpdateSavedSearchResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = ErrorCode.Success
        };
    }

    public async Task<Envelope> DeleteSavedSearchAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var deleteError = DeserializePayload<DeleteSavedSearchRequest>(request, out var deleteReq);
        if (deleteError != null) return deleteError;

        if (!Guid.TryParse(deleteReq.Id, out var id))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid ID");

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var saved = await db.SavedSearches.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (saved == null)
            return ErrorResponse(request, ErrorCode.NotFound, "Saved search not found");

        db.SavedSearches.Remove(saved);
        await db.SaveChangesAsync(ct);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.DeleteSavedSearchResponse,
            StatusCode = ErrorCode.Success
        };
    }

    public async Task<Envelope> PinSavedSearchAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var pinError = DeserializePayload<PinSavedSearchRequest>(request, out var pinReq);
        if (pinError != null) return pinError;

        if (!Guid.TryParse(pinReq.Id, out var id))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid ID");

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var saved = await db.SavedSearches.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (saved == null)
            return ErrorResponse(request, ErrorCode.NotFound, "Saved search not found");

        saved.IsPinned = pinReq.IsPinned;
        saved.ModifiedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.PinSavedSearchResponse,
            StatusCode = ErrorCode.Success
        };
    }


}
