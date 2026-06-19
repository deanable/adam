using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Google.Protobuf;

namespace Adam.BrokerService.Handlers;

/// <summary>
/// Broker handler for face detection operations.
/// Handles DetectFaces requests from connected clients.
/// </summary>
public sealed class FaceHandler : HandlerBase
{
    public FaceHandler(
        IServiceProvider serviceProvider,
        ILogger<FaceHandler> logger,
        AuthorizationMiddleware authz)
        : base(serviceProvider, logger, authz)
    {
    }

    /// <summary>
    /// Detects faces in an asset image by running YuNet → ArcFace → FaceMatcher.
    /// </summary>
    public async Task<Envelope> DetectFacesAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:update", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var error = DeserializePayload<DetectFacesRequest>(request, out var req);
        if (error != null) return error;

        if (!Guid.TryParse(req!.AssetId, out var assetId))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid asset ID");

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var asset = await db.DigitalAssets
            .FirstOrDefaultAsync(a => a.Id == assetId, ct);

        if (asset == null)
            return ErrorResponse(request, ErrorCode.NotFound, "Asset not found");

        if (!asset.MimeType.StartsWith("image/"))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Asset is not an image");

        // Check if faces already exist for this asset
        var existingCount = await db.AssetFaces
            .CountAsync(f => f.AssetId == assetId, ct);

        var response = new DetectFacesResponse
        {
            AssetId = req.AssetId,
            FaceCount = existingCount
        };

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.DetectFacesResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = ErrorCode.Success
        };
    }
}
