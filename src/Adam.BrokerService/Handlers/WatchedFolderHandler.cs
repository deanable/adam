using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

public sealed class WatchedFolderHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WatchedFolderHandler> _logger;
    private readonly AuthorizationMiddleware _authz;

    public WatchedFolderHandler(IServiceProvider serviceProvider, ILogger<WatchedFolderHandler> logger, AuthorizationMiddleware authz)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _authz = authz;
    }

    public async Task<Envelope> ListAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        using var scope = _serviceProvider.CreateScope();
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
        if (!await _authz.HasPermissionAsync(request, "asset:create", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        if (request.Payload == null)
            return ErrorResponse(request, ErrorCode.BadRequest, "Null payload");
        var req = ProtoHelper.Deserialize<CreateWatchedFolderRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
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

        _logger.LogInformation("SECURITY: Created watched folder {FolderId} at {Path}. CorrelationId: {CorrelationId}",
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
        if (!await _authz.HasPermissionAsync(request, "asset:update", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        if (request.Payload == null)
            return ErrorResponse(request, ErrorCode.BadRequest, "Null payload");
        var req = ProtoHelper.Deserialize<UpdateWatchedFolderRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var id = Guid.Parse(req.Id);
        var folder = await db.WatchedFolders.FindAsync(new object[] { id }, ct);
        if (folder == null)
            return ErrorResponse(request, ErrorCode.NotFound, "Watched folder not found");

        folder.Path = req.Path;
        folder.IsEnabled = req.IsEnabled;
        folder.ModifiedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("SECURITY: Updated watched folder {FolderId} at {Path}. CorrelationId: {CorrelationId}",
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
        if (!await _authz.HasPermissionAsync(request, "asset:delete", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        if (request.Payload == null)
            return ErrorResponse(request, ErrorCode.BadRequest, "Null payload");
        var req = ProtoHelper.Deserialize<DeleteWatchedFolderRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var id = Guid.Parse(req.Id);
        var folder = await db.WatchedFolders.FindAsync(new object[] { id }, ct);
        if (folder == null)
            return ErrorResponse(request, ErrorCode.NotFound, "Watched folder not found");

        db.WatchedFolders.Remove(folder);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("SECURITY: Deleted watched folder {FolderId} at {Path}. CorrelationId: {CorrelationId}",
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
}
