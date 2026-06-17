using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

public sealed class WatchedFolderHandler : HandlerBase
{
    public WatchedFolderHandler(IServiceProvider serviceProvider, ILogger<WatchedFolderHandler> logger, AuthorizationMiddleware authz)
        : base(serviceProvider, logger, authz)
    {
    }

    public async Task<Envelope> ListAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var folders = await db.WatchedFolders
            .AsNoTracking()
            .OrderBy(w => w.Path)
            .ToListAsync(ct);

        var response = new ListWatchedFoldersResponse();
        foreach (var f in folders)
        {
            response.Folders.Add(new WatchedFolderInfo
            {
                Id = f.Id.ToString(),
                Path = f.Path,
                IsEnabled = f.IsEnabled
            });
        }

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.ListWatchedFoldersResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = ErrorCode.Success
        };
    }

    public async Task<Envelope> CreateAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:create", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var createError = DeserializePayload<CreateWatchedFolderRequest>(request, out var req);
        if (createError != null) return createError;

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var folder = new Adam.Shared.Models.WatchedFolder
        {
            Id = Guid.NewGuid(),
            Path = req.Path,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        };

        db.WatchedFolders.Add(folder);
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("SECURITY: Created watched folder {FolderId} at {Path}. CorrelationId: {CorrelationId}",
            folder.Id, folder.Path, request.CorrelationId);

        var response = new CreateWatchedFolderResponse
        {
            Id = folder.Id.ToString(),
            Path = folder.Path
        };

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.CreateWatchedFolderResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = ErrorCode.Success
        };
    }

    public async Task<Envelope> UpdateAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:update", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var updateError = DeserializePayload<UpdateWatchedFolderRequest>(request, out var updateReq);
        if (updateError != null) return updateError;

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var id = Guid.Parse(updateReq.Id);
        var folder = await db.WatchedFolders.FindAsync(new object[] { id }, ct);
        if (folder == null)
            return ErrorResponse(request, ErrorCode.NotFound, "Watched folder not found");

        folder.Path = updateReq.Path;
        folder.IsEnabled = updateReq.IsEnabled;
        folder.ModifiedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("SECURITY: Updated watched folder {FolderId} at {Path}. CorrelationId: {CorrelationId}",
            folder.Id, folder.Path, request.CorrelationId);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.UpdateAssetResponse,
            StatusCode = ErrorCode.Success
        };
    }

    public async Task<Envelope> DeleteAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:delete", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var deleteError = DeserializePayload<DeleteWatchedFolderRequest>(request, out var deleteReq);
        if (deleteError != null) return deleteError;

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var id = Guid.Parse(deleteReq.Id);
        var folder = await db.WatchedFolders.FindAsync(new object[] { id }, ct);
        if (folder == null)
            return ErrorResponse(request, ErrorCode.NotFound, "Watched folder not found");

        db.WatchedFolders.Remove(folder);
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("SECURITY: Deleted watched folder {FolderId} at {Path}. CorrelationId: {CorrelationId}",
            folder.Id, folder.Path, request.CorrelationId);

        var response = new DeleteWatchedFolderResponse { Success = true };
        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.DeleteWatchedFolderResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = ErrorCode.Success
        };
    }


}
