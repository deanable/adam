using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
using Adam.Shared.Services;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

/// <summary>
/// Broker handler for smart search ranking operations.
/// Handles click logging and re-ranking requests from connected clients.
/// </summary>
public sealed class SearchRankingHandler : HandlerBase
{
    public SearchRankingHandler(
        IServiceProvider serviceProvider,
        ILogger<SearchRankingHandler> logger,
        AuthorizationMiddleware authz)
        : base(serviceProvider, logger, authz)
    {
    }

    /// <summary>
    /// Logs a search click event from a client.
    /// </summary>
    public async Task<Envelope> LogClickAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var error = DeserializePayload<LogSearchClickRequest>(request, out var req);
        if (error != null) return error;

        if (!Guid.TryParse(req.AssetId, out var assetId))
            return ErrorResponse(request, ErrorCode.InvalidArgument, "Invalid asset ID");

        using var scope = ServiceProvider.CreateScope();
        var rankingService = scope.ServiceProvider.GetRequiredService<SearchRankingService>();

        try
        {
            var logId = await rankingService.LogClickAsync(
                assetId, req.QueryText, req.RankPosition, req.DwellTimeMs, ct);

            var response = new LogSearchClickResponse
            {
                Id = logId.ToString()
            };

            return new Envelope
            {
                CorrelationId = request.CorrelationId,
                MessageType = MessageTypeCode.LogSearchClickResponse,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
                StatusCode = ErrorCode.Success
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "LogClick failed for asset {AssetId}", req.AssetId);
            return ErrorResponse(request, ErrorCode.InternalError, "LogClick failed");
        }
    }

    /// <summary>
    /// Re-ranks search results based on click history affinity.
    /// </summary>
    public async Task<Envelope> ReRankAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, ErrorCode.Forbidden, "Forbidden");

        var error = DeserializePayload<ReRankRequest>(request, out var req);
        if (error != null) return error;

        using var scope = ServiceProvider.CreateScope();
        var rankingService = scope.ServiceProvider.GetRequiredService<SearchRankingService>();

        // We need SemanticSearchService to build search results for re-ranking.
        // In the broker, the client sends the original results as wire items.
        // We convert them to SemanticSearchResults and then re-rank.
        try
        {
            // The client sends results as RankedAssetWire items. We re-rank without
            // needing the full DigitalAsset — the ranking service returns IDs + scores.
            // Convert wire results to a lookup for matching.
            var resultMap = req.Results.ToDictionary(
                r => Guid.Parse(r.AssetId),
                r => r.OriginalScore);

            // Build minimal SemanticSearchResult list from wire data
            var searchResults = req.Results
                .Select(r =>
                {
                    var assetId = Guid.Parse(r.AssetId);
                    return new SemanticSearchResult
                    {
                        Asset = new Adam.Shared.Models.DigitalAsset { Id = assetId },
                        Score = r.OriginalScore,
                        Rank = 0
                    };
                })
                .ToList();

            var ranked = await rankingService.ReRankAsync(searchResults, req.Query, ct);

            var response = new ReRankResponse();
            foreach (var r in ranked)
            {
                response.Results.Add(new RankedResultWire
                {
                    AssetId = r.AssetId.ToString(),
                    CombinedScore = r.CombinedScore,
                    ClickBoost = r.ClickBoost
                });
            }

            return new Envelope
            {
                CorrelationId = request.CorrelationId,
                MessageType = MessageTypeCode.ReRankResponse,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
                StatusCode = ErrorCode.Success
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ReRank failed for query: {Query}", req.Query);
            return ErrorResponse(request, ErrorCode.InternalError, "ReRank failed");
        }
    }
}
