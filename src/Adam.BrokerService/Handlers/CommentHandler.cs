using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

public sealed class CommentHandler : HandlerBase
{
    private readonly AuthHandler _authHandler;
    private readonly ChangeNotificationService _notificationService;

    public CommentHandler(
        IServiceProvider serviceProvider,
        ILogger<CommentHandler> logger,
        AuthorizationMiddleware authz,
        AuthHandler authHandler,
        ChangeNotificationService notificationService)
        : base(serviceProvider, logger, authz)
    {
        _authHandler = authHandler;
        _notificationService = notificationService;
    }

    public async Task<Envelope> ListCommentsAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var error = DeserializePayload<ListCommentsRequest>(request, out var req);
        if (error != null) return error;

        if (!Guid.TryParse(req.AssetId, out var assetId))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid asset ID");

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Load to memory first, then sort — SQLite cannot ORDER BY DateTimeOffset
        var comments = (await db.Comments
            .Include(c => c.User)
            .Where(c => c.AssetId == assetId)
            .ToListAsync(ct))
            .OrderBy(c => c.CreatedAt)
            .ToList();

        var userId = _authHandler.GetUserId(request);
        var userIdGuid = Guid.TryParse(userId, out var uid) ? uid : Guid.Empty;

        var response = new ListCommentsResponse
        {
            TotalCount = comments.Count
        };

        // Build tree: top-level first, then attach replies
        var topLevel = comments.Where(c => c.ParentCommentId == null).ToList();
        foreach (var comment in topLevel)
        {
            var wire = MapToWire(comment, userIdGuid);
            wire.Replies.AddRange(
                comments
                    .Where(c => c.ParentCommentId == comment.Id)
                    .Select(c => MapToWire(c, userIdGuid)));
            response.Comments.Add(wire);
        }

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.ListCommentsResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = ErrorCode.Success
        };
    }

    public async Task<Envelope> CreateCommentAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:update", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var error = DeserializePayload<CreateCommentRequest>(request, out var req);
        if (error != null) return error;

        if (string.IsNullOrWhiteSpace(req.Body))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Comment body cannot be empty");

        if (!Guid.TryParse(req.AssetId, out var assetId))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid asset ID");

        Guid? parentId = null;
        if (!string.IsNullOrEmpty(req.ParentCommentId))
        {
            if (!Guid.TryParse(req.ParentCommentId, out var pid))
                return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid parent comment ID");
            parentId = pid;
        }

        var userId = _authHandler.GetUserId(request);
        if (!Guid.TryParse(userId, out var userGuid))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid user ID");

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Verify asset exists
        var assetExists = await db.DigitalAssets.AnyAsync(a => a.Id == assetId, ct);
        if (!assetExists)
            return ErrorResponse(request, ErrorCode.NotFound, "Asset not found");

        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            ParentCommentId = parentId,
            UserId = userGuid,
            Body = req.Body.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);

        var createdAt = comment.CreatedAt.ToUnixTimeSeconds();

        // Broadcast CommentNotification to other clients
        _ = BroadcastCommentNotificationAsync(comment.Id.ToString(), assetId.ToString(), "created", request.ConnectionId);

        var response = new CreateCommentResponse
        {
            Id = comment.Id.ToString(),
            CreatedAt = createdAt
        };

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.CreateCommentResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = ErrorCode.Success
        };
    }

    public async Task<Envelope> UpdateCommentAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:update", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var error = DeserializePayload<UpdateCommentRequest>(request, out var req);
        if (error != null) return error;

        if (string.IsNullOrWhiteSpace(req.Body))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Comment body cannot be empty");

        if (!Guid.TryParse(req.CommentId, out var commentId))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid comment ID");

        var userId = _authHandler.GetUserId(request);
        if (!Guid.TryParse(userId, out var userGuid))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid user ID");

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == commentId, ct);
        if (comment == null)
            return ErrorResponse(request, ErrorCode.NotFound, "Comment not found");

        // Only the author can edit
        if (comment.UserId != userGuid)
            return ErrorResponse(request, ErrorCode.Forbidden, "Only the author can edit this comment");

        comment.Body = req.Body.Trim();
        comment.EditedAt = DateTimeOffset.UtcNow;
        comment.Version++;
        await db.SaveChangesAsync(ct);

        var editedAt = comment.EditedAt.Value.ToUnixTimeSeconds();

        // Broadcast CommentNotification
        _ = BroadcastCommentNotificationAsync(comment.Id.ToString(), comment.AssetId.ToString(), "updated", request.ConnectionId);

        var response = new UpdateCommentResponse
        {
            Id = comment.Id.ToString(),
            EditedAt = editedAt
        };

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.UpdateCommentResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = ErrorCode.Success
        };
    }

    public async Task<Envelope> DeleteCommentAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:update", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var error = DeserializePayload<DeleteCommentRequest>(request, out var req);
        if (error != null) return error;

        if (!Guid.TryParse(req.CommentId, out var commentId))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid comment ID");

        var userId = _authHandler.GetUserId(request);
        if (!Guid.TryParse(userId, out var userGuid))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid user ID");

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == commentId, ct);
        if (comment == null)
            return ErrorResponse(request, ErrorCode.NotFound, "Comment not found");

        // Check ownership: author or Administrator/Editor can delete
        var roleName = _authHandler.GetUserRole(request);
        var isAdminOrEditor = roleName is "Administrator" or "Editor";

        if (comment.UserId != userGuid && !isAdminOrEditor)
            return ErrorResponse(request, ErrorCode.Forbidden, "Not authorized to delete this comment");

        comment.IsDeleted = true;
        await db.SaveChangesAsync(ct);

        // Broadcast CommentNotification
        _ = BroadcastCommentNotificationAsync(comment.Id.ToString(), comment.AssetId.ToString(), "deleted", request.ConnectionId);

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.DeleteCommentResponse,
            StatusCode = ErrorCode.Success
        };
    }

    // ─── Helpers ───

    private CommentWire MapToWire(Comment comment, Guid currentUserId)
    {
        var canEdit = comment.UserId == currentUserId;
        var canDelete = comment.UserId == currentUserId;

        return new CommentWire
        {
            Id = comment.Id.ToString(),
            AssetId = comment.AssetId.ToString(),
            ParentCommentId = comment.ParentCommentId?.ToString(),
            Body = comment.IsDeleted ? "[deleted]" : comment.Body,
            UserName = comment.User?.Username ?? "Unknown",
            UserId = comment.UserId.ToString(),
            CreatedAt = comment.CreatedAt.ToUnixTimeSeconds(),
            EditedAt = comment.EditedAt?.ToUnixTimeSeconds() ?? 0,
            IsDeleted = comment.IsDeleted,
            CanEdit = canEdit,
            CanDelete = canDelete
        };
    }

    private async Task BroadcastCommentNotificationAsync(string commentId, string assetId, string action, string? excludeConnectionId)
    {
        try
        {
            var notification = new CommentNotification
            {
                AssetId = assetId,
                CommentId = commentId,
                Action = action,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            var envelope = new Envelope
            {
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.CommentNotification,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(notification))
            };

            // Use ConnectionRegistry directly for the notification broadcast
            // This is a separate notification type from the ChangeNotification system
            await _notificationService.BroadcastAsync(
                $"comment:{commentId}",
                $"comment:{action}",
                _authHandler.GetUserId(new Envelope { AuthToken = string.Empty }) ?? string.Empty,
                excludeConnectionId);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to broadcast comment notification");
        }
    }


}
