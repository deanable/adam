using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.Shared.Services;

/// <summary>
/// Dual-mode service for comment CRUD.
/// Standalone: Direct AppDbContext access.
/// Multi-user: Sends protobuf envelopes via BrokerClient.
/// </summary>
public sealed class CommentService
{
    private readonly ModeManager _modeManager;
    private readonly ILogger<CommentService> _logger;

    public CommentService(ModeManager modeManager, ILogger<CommentService> logger)
    {
        _modeManager = modeManager;
        _logger = logger;
    }

    /// <summary>
    /// Lists all non-deleted comments for an asset as a flat list sorted by CreatedAt.
    /// The caller (ViewModel) groups into a tree.
    /// </summary>
    public async Task<List<CommentDto>> ListCommentsAsync(Guid assetId, CancellationToken ct = default)
    {
        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct);
            // Load all comments for the asset and sort in-memory to avoid
            // SQLite's inability to ORDER BY DateTimeOffset.
            var comments = await db.Comments
                .Include(c => c.User)
                .Where(c => c.AssetId == assetId)
                .ToListAsync(ct);

            return comments
                .OrderBy(c => c.CreatedAt)
                .Select(c => MapToDto(c, c.User.Username, canEdit: true, canDelete: true))
                .ToList();
        }
        else
        {
            var broker = _modeManager.BrokerClient;
            var auth = _modeManager.AuthSession;
            if (broker == null || auth == null)
                return [];

            if (!broker.IsConnected)
                await broker.ConnectAsync(ct);

            var reqMsg = new ListCommentsRequest { AssetId = assetId.ToString() };
            var req = new Envelope
            {
                AuthToken = auth.Token ?? string.Empty,
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.ListCommentsRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(reqMsg))
            };

            var resp = await broker.SendAsync(req, ct);
            if (resp.StatusCode != 0)
            {
                _logger.LogWarning("ListComments failed: {Error} (code {Code})", resp.ErrorMessage, resp.StatusCode);
                return [];
            }

            var listResp = ProtoHelper.Deserialize<ListCommentsResponse>(resp.Payload.ToByteArray());
            return listResp.Comments.Select(MapWireToDto).ToList();
        }
    }

    /// <summary>
    /// Creates a new comment (top-level or reply).
    /// </summary>
    public async Task<CommentDto> CreateCommentAsync(Guid assetId, Guid? parentId, string body, Guid userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Comment body cannot be empty", nameof(body));

        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct);

            var comment = new Comment
            {
                Id = Guid.NewGuid(),
                AssetId = assetId,
                ParentCommentId = parentId,
                UserId = userId,
                Body = body.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
                Version = 1
            };

            db.Comments.Add(comment);
            await db.SaveChangesAsync(ct);

            // Reload with User navigation
            var saved = await db.Comments
                .Include(c => c.User)
                .FirstAsync(c => c.Id == comment.Id, ct);

            return MapToDto(saved, saved.User.Username, canEdit: true, canDelete: true);
        }
        else
        {
            var broker = _modeManager.BrokerClient;
            var auth = _modeManager.AuthSession;
            if (broker == null || auth == null)
                throw new InvalidOperationException("Not connected to broker");

            if (!broker.IsConnected)
                await broker.ConnectAsync(ct);

            var reqMsg = new CreateCommentRequest
            {
                AssetId = assetId.ToString(),
                ParentCommentId = parentId?.ToString(),
                Body = body.Trim()
            };

            var req = new Envelope
            {
                AuthToken = auth.Token ?? string.Empty,
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.CreateCommentRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(reqMsg))
            };

            var resp = await broker.SendAsync(req, ct);
            if (resp.StatusCode != 0)
                throw new InvalidOperationException($"CreateComment failed: {resp.ErrorMessage}");

            var createResp = ProtoHelper.Deserialize<CreateCommentResponse>(resp.Payload.ToByteArray());
            var currentUser = auth.CurrentUser;

            return new CommentDto(
                createResp.Id,
                assetId.ToString(),
                parentId?.ToString(),
                body.Trim(),
                currentUser?.Username ?? "Unknown",
                createResp.CreatedAt,
                null,
                CanEdit: true,
                CanDelete: true,
                Replies: []);
        }
    }

    /// <summary>
    /// Updates a comment body. Verifies ownership.
    /// </summary>
    public async Task<CommentDto?> UpdateCommentAsync(Guid commentId, string body, Guid userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Comment body cannot be empty", nameof(body));

        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct);

            var comment = await db.Comments
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == commentId, ct);

            if (comment == null)
                return null;

            if (comment.UserId != userId)
                return null;

            comment.Body = body.Trim();
            comment.EditedAt = DateTimeOffset.UtcNow;
            comment.Version++;
            await db.SaveChangesAsync(ct);

            return MapToDto(comment, comment.User.Username, canEdit: true, canDelete: true);
        }
        else
        {
            var broker = _modeManager.BrokerClient;
            var auth = _modeManager.AuthSession;
            if (broker == null || auth == null) return null;

            if (!broker.IsConnected) await broker.ConnectAsync(ct);

            var reqMsg = new UpdateCommentRequest
            {
                CommentId = commentId.ToString(),
                Body = body.Trim()
            };

            var req = new Envelope
            {
                AuthToken = auth.Token ?? string.Empty,
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.UpdateCommentRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(reqMsg))
            };

            var resp = await broker.SendAsync(req, ct);
            if (resp.StatusCode != 0) return null;

            var updateResp = ProtoHelper.Deserialize<UpdateCommentResponse>(resp.Payload.ToByteArray());
            var currentUser = auth.CurrentUser;

            return new CommentDto(
                commentId.ToString(),
                string.Empty,
                null,
                body.Trim(),
                currentUser?.Username ?? "Unknown",
                0,
                updateResp.EditedAt,
                CanEdit: true,
                CanDelete: true,
                Replies: []);
        }
    }

    /// <summary>
    /// Soft-deletes a comment. Verifies ownership.
    /// </summary>
    public async Task<bool> DeleteCommentAsync(Guid commentId, Guid userId, CancellationToken ct = default)
    {
        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct);

            var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == commentId, ct);
            if (comment == null) return false;

            if (comment.UserId != userId) return false;

            comment.IsDeleted = true;
            await db.SaveChangesAsync(ct);
            return true;
        }
        else
        {
            var broker = _modeManager.BrokerClient;
            var auth = _modeManager.AuthSession;
            if (broker == null || auth == null) return false;

            if (!broker.IsConnected) await broker.ConnectAsync(ct);

            var reqMsg = new DeleteCommentRequest { CommentId = commentId.ToString() };
            var req = new Envelope
            {
                AuthToken = auth.Token ?? string.Empty,
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.DeleteCommentRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(reqMsg))
            };

            var resp = await broker.SendAsync(req, ct);
            return resp.StatusCode == 0;
        }
    }

    /// <summary>
    /// Returns the total count of non-deleted comments for an asset.
    /// </summary>
    public async Task<int> CountCommentsAsync(Guid assetId, CancellationToken ct = default)
    {
        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct);
            return await db.Comments.CountAsync(c => c.AssetId == assetId, ct);
        }
        else
        {
            // For multi-user: list all and count
            var comments = await ListCommentsAsync(assetId, ct);
            return comments.Count;
        }
    }

    // ─── DTO Mapping ───────────────────────────────────────────

    private static CommentDto MapToDto(Comment c, string username, bool canEdit, bool canDelete)
    {
        return new CommentDto(
            c.Id.ToString(),
            c.AssetId.ToString(),
            c.ParentCommentId?.ToString(),
            c.IsDeleted ? "[deleted]" : c.Body,
            username,
            c.CreatedAt.ToUnixTimeSeconds(),
            c.EditedAt?.ToUnixTimeSeconds(),
            canEdit,
            canDelete,
            []);
    }

    private static CommentDto MapWireToDto(CommentWire w)
    {
        return new CommentDto(
            w.Id,
            w.AssetId,
            w.ParentCommentId,
            w.IsDeleted ? "[deleted]" : w.Body,
            w.UserName,
            w.CreatedAt,
            w.EditedAt != 0 ? w.EditedAt : null,
            w.CanEdit,
            w.CanDelete,
            w.Replies.Select(MapWireToDto).ToList());
    }
}

/// <summary>
/// DTO for comment data used in UI binding.
/// </summary>
public sealed record CommentDto(
    string Id,
    string AssetId,
    string? ParentCommentId,
    string Body,
    string Username,
    long CreatedAtUnix,
    long? EditedAtUnix,
    bool CanEdit,
    bool CanDelete,
    List<CommentDto> Replies);
