using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

/// <summary>
/// Broker handler for semantic search and visual similarity operations.
/// Delegates to SemanticSearchService for embedding computation and similarity ranking.
/// </summary>
public sealed class SemanticSearchHandler : HandlerBase
{
    public SemanticSearchHandler(
        IServiceProvider serviceProvider,
        ILogger<SemanticSearchHandler> logger,
        AuthorizationMiddleware authz)
        : base(serviceProvider, logger, authz)
    {
    }

    public async Task<Envelope> SearchByTextAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var error = DeserializePayload<SemanticSearchRequest>(request, out var req);
        if (error != null) return error;

        if (string.IsNullOrWhiteSpace(req.Query))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Query cannot be empty");

        using var scope = ServiceProvider.CreateScope();
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
            Logger.LogError(ex, "Semantic search failed for query: {Query}", req.Query);
            return ErrorResponse(request, ErrorCode.InternalError, "Semantic search failed");
        }
    }

    public async Task<Envelope> FindSimilarAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var error = DeserializePayload<FindSimilarRequest>(request, out var req);
        if (error != null) return error;

        if (!Guid.TryParse(req.AssetId, out var assetId))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid asset ID");

        using var scope = ServiceProvider.CreateScope();
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
            Logger.LogError(ex, "FindSimilar failed for asset {AssetId}", req.AssetId);
            return ErrorResponse(request, ErrorCode.InternalError, "FindSimilar failed");
        }
    }

    public async Task<Envelope> RecomputeEmbeddingsAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:*", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        using var scope = ServiceProvider.CreateScope();
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
            Logger.LogError(ex, "RecomputeEmbeddings failed");
            return ErrorResponse(request, ErrorCode.InternalError, "RecomputeEmbeddings failed");
        }
    }
}
