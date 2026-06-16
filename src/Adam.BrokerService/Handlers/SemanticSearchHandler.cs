using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

/// <summary>
/// Broker handler for semantic search and visual similarity operations.
/// Delegates to SemanticSearchService for embedding computation and similarity ranking.
/// </summary>
public sealed class SemanticSearchHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SemanticSearchHandler> _logger;
    private readonly AuthorizationMiddleware _authz;

    public SemanticSearchHandler(
        IServiceProvider serviceProvider,
        ILogger<SemanticSearchHandler> logger,
        AuthorizationMiddleware authz)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _authz = authz;
    }

    public async Task<Envelope> SearchByTextAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        if (request.Payload == null)
            return ErrorResponse(request, ErrorCode.BadRequest, "Null payload");

        SemanticSearchRequest req;
        try
        {
            req = ProtoHelper.Deserialize<SemanticSearchRequest>(request.Payload.ToByteArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize {MessageType}", request.MessageType);
            return ErrorResponse(request, ErrorCode.BadRequest, "Malformed request payload");
        }

        if (string.IsNullOrWhiteSpace(req.Query))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Query cannot be empty");

        using var scope = _serviceProvider.CreateScope();
        var semanticSearch = scope.ServiceProvider.GetRequiredService<SemanticSearchService>();
        var historyHandler = scope.ServiceProvider.GetService<SearchHistoryHandler>();

        // Record search history if requested (fire-and-forget)
        if (req.RecordHistory && historyHandler != null)
        {
            var recordReq = new Envelope
            {
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new RecordSearchHistoryRequest
                {
                    QueryText = req.Query,
                    IsSemantic = true
                }))
            };
            _ = historyHandler.RecordSearchHistoryAsync(recordReq, ct);
        }

        try
        {
            var results = await semanticSearch.SearchByTextAsync(
                req.Query, req.MaxResults, req.MinScore, ct);

            var response = new SemanticSearchResponse();
            foreach (var r in results)
            {
                response.Results.Add(new SemanticSearchResultWire
                {
                    AssetId = r.Asset.Id.ToString(),
                    Title = r.Asset.Title,
                    FileName = r.Asset.FileName,
                    MimeType = r.Asset.MimeType,
                    FileSize = r.Asset.FileSize,
                    CreatedAt = r.Asset.CreatedAt.ToUnixTimeSeconds(),
                    Score = r.Score,
                    Rank = r.Rank
                });
            }

            return new Envelope
            {
                CorrelationId = request.CorrelationId,
                MessageType = MessageTypeCode.SemanticSearchResponse,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
                StatusCode = ErrorCode.Success
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Semantic search failed for query: {Query}", req.Query);
            return ErrorResponse(request, ErrorCode.InternalError, "Semantic search failed");
        }
    }

    public async Task<Envelope> FindSimilarAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        if (request.Payload == null)
            return ErrorResponse(request, ErrorCode.BadRequest, "Null payload");

        FindSimilarRequest req;
        try
        {
            req = ProtoHelper.Deserialize<FindSimilarRequest>(request.Payload.ToByteArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize {MessageType}", request.MessageType);
            return ErrorResponse(request, ErrorCode.BadRequest, "Malformed request payload");
        }

        if (!Guid.TryParse(req.AssetId, out var assetId))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid asset ID");

        using var scope = _serviceProvider.CreateScope();
        var semanticSearch = scope.ServiceProvider.GetRequiredService<SemanticSearchService>();

        try
        {
            var results = await semanticSearch.FindSimilarAsync(
                assetId, req.MaxResults, req.MinScore, ct);

            var response = new FindSimilarResponse();
            foreach (var r in results)
            {
                response.Results.Add(new SemanticSearchResultWire
                {
                    AssetId = r.Asset.Id.ToString(),
                    Title = r.Asset.Title,
                    FileName = r.Asset.FileName,
                    MimeType = r.Asset.MimeType,
                    FileSize = r.Asset.FileSize,
                    CreatedAt = r.Asset.CreatedAt.ToUnixTimeSeconds(),
                    Score = r.Score,
                    Rank = r.Rank
                });
            }

            return new Envelope
            {
                CorrelationId = request.CorrelationId,
                MessageType = MessageTypeCode.FindSimilarResponse,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
                StatusCode = ErrorCode.Success
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FindSimilar failed for asset {AssetId}", req.AssetId);
            return ErrorResponse(request, ErrorCode.InternalError, "FindSimilar failed");
        }
    }

    public async Task<Envelope> RecomputeEmbeddingsAsync(Envelope request, CancellationToken ct)
    {
        if (!await _authz.HasPermissionAsync(request, "asset:*", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        using var scope = _serviceProvider.CreateScope();
        var embeddingService = scope.ServiceProvider.GetRequiredService<EmbeddingService>();

        try
        {
            await embeddingService.EnsureInitializedAsync(ct);

            var pending = await embeddingService.GetPendingCountAsync();
            if (pending == 0)
            {
                return new Envelope
                {
                    CorrelationId = request.CorrelationId,
                    MessageType = MessageTypeCode.RecomputeEmbeddingsResponse,
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new RecomputeEmbeddingsResponse { TotalProcessed = 0 })),
                    StatusCode = ErrorCode.Success
                };
            }

            await embeddingService.ComputeAllEmbeddingsAsync(progress: null, ct);

            return new Envelope
            {
                CorrelationId = request.CorrelationId,
                MessageType = MessageTypeCode.RecomputeEmbeddingsResponse,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new RecomputeEmbeddingsResponse { TotalProcessed = pending })),
                StatusCode = ErrorCode.Success
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RecomputeEmbeddings failed");
            return ErrorResponse(request, ErrorCode.InternalError, "RecomputeEmbeddings failed");
        }
    }

    private static Envelope ErrorResponse(Envelope request, int code, string message)
    {
        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = request.MessageType,
            StatusCode = code,
            ErrorMessage = message
        };
    }
}
