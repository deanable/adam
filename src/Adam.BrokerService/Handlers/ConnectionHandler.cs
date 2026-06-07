using System.Diagnostics;
using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
using Adam.Shared.Services;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

public sealed class ConnectionHandler : IConnectionHandler
{
    private readonly AuthHandler _authHandler;
    private readonly AssetHandler _assetHandler;
    private readonly CollectionHandler _collectionHandler;
    private readonly ChangeHandler _changeHandler;
    private readonly UserHandler _userHandler;
    private readonly AuditLogHandler _auditLogHandler;
    private readonly StatusHandler _statusHandler;
    private readonly SidebarHandler _sidebarHandler;
    private readonly WatchedFolderHandler _watchedFolderHandler;
    private readonly ILogger<ConnectionHandler> _logger;

    public ConnectionHandler(
        AuthHandler authHandler,
        AssetHandler assetHandler,
        CollectionHandler collectionHandler,
        ChangeHandler changeHandler,
        UserHandler userHandler,
        AuditLogHandler auditLogHandler,
        StatusHandler statusHandler,
        SidebarHandler sidebarHandler,
        WatchedFolderHandler watchedFolderHandler,
        ILogger<ConnectionHandler> logger)
    {
        _authHandler = authHandler;
        _assetHandler = assetHandler;
        _collectionHandler = collectionHandler;
        _changeHandler = changeHandler;
        _userHandler = userHandler;
        _auditLogHandler = auditLogHandler;
        _statusHandler = statusHandler;
        _sidebarHandler = sidebarHandler;
        _watchedFolderHandler = watchedFolderHandler;
        _logger = logger;
    }

    public async Task<Envelope> HandleAsync(Envelope request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        ConnectionDebugLogger.Trace($"[DISPATCH] conn={request.ConnectionId}, type={request.MessageType}, corrId={request.CorrelationId}");

        try
        {
            Envelope response;
            switch (request.MessageType)
            {
                case MessageTypeCode.LoginRequest:
                    ConnectionDebugLogger.Info($"[DISPATCH] LoginRequest from conn={request.ConnectionId}");
                    response = await _authHandler.LoginAsync(request, ct);
                    break;
                case MessageTypeCode.ValidateTokenRequest:
                    response = _authHandler.ValidateToken(request);
                    break;
                case MessageTypeCode.ListAssetsRequest:
                    response = await _assetHandler.ListAssetsAsync(request, ct);
                    break;
                case MessageTypeCode.GetFileRequest:
                    response = await _assetHandler.GetFileAsync(request, ct);
                    break;
                case MessageTypeCode.GetFileChunkRequest:
                    response = await _assetHandler.GetFileChunkAsync(request, ct);
                    break;
                case MessageTypeCode.GetAssetRequest:
                    response = await _assetHandler.GetAssetAsync(request, ct);
                    break;
                case MessageTypeCode.CreateAssetRequest:
                    response = await _assetHandler.CreateAssetAsync(request, ct);
                    break;
                case MessageTypeCode.UpdateAssetRequest:
                    response = await _assetHandler.UpdateAssetAsync(request, ct);
                    break;
                case MessageTypeCode.DeleteAssetRequest:
                    response = await _assetHandler.DeleteAssetAsync(request, ct);
                    break;
                case MessageTypeCode.CreateCollectionRequest:
                    response = await _collectionHandler.CreateCollectionAsync(request, ct);
                    break;
                case MessageTypeCode.ListCollectionsRequest:
                    response = await _collectionHandler.ListCollectionsAsync(request, ct);
                    break;
                case MessageTypeCode.GetChangesRequest:
                    response = await _changeHandler.GetChangesAsync(request, ct);
                    break;
                case MessageTypeCode.ListUsersRequest:
                    response = await _userHandler.ListUsersAsync(request, ct);
                    break;
                case MessageTypeCode.ListRolesRequest:
                    response = await _userHandler.ListRolesAsync(request, ct);
                    break;
                case MessageTypeCode.CreateUserRequest:
                    response = await _userHandler.CreateUserAsync(request, ct);
                    break;
                case MessageTypeCode.UpdateUserRequest:
                    response = await _userHandler.UpdateUserAsync(request, ct);
                    break;
                case MessageTypeCode.DeleteUserRequest:
                    response = await _userHandler.DeleteUserAsync(request, ct);
                    break;
                case MessageTypeCode.ListAuditLogsRequest:
                    response = await _auditLogHandler.ListAuditLogsAsync(request, ct);
                    break;
                case MessageTypeCode.GetServiceStatusRequest:
                    response = await _statusHandler.GetStatusAsync(request, ct);
                    break;
                case MessageTypeCode.StartServiceRequest:
                    response = await _statusHandler.StartServiceAsync(request, ct);
                    break;
                case MessageTypeCode.StopServiceRequest:
                    response = await _statusHandler.StopServiceAsync(request, ct);
                    break;
                case MessageTypeCode.ListFoldersRequest:
                    response = await _sidebarHandler.ListFoldersAsync(request, ct);
                    break;
                case MessageTypeCode.ListKeywordsRequest:
                    response = await _sidebarHandler.ListKeywordsAsync(request, ct);
                    break;
                case MessageTypeCode.ListMediaFormatCountsRequest:
                    response = await _sidebarHandler.ListMediaFormatCountsAsync(request, ct);
                    break;
                case MessageTypeCode.ListMetadataCategoriesRequest:
                    response = await _sidebarHandler.ListMetadataCategoriesAsync(request, ct);
                    break;
                case MessageTypeCode.ListDateTakenTreeRequest:
                    response = await _sidebarHandler.ListDateTakenTreeAsync(request, ct);
                    break;
                case MessageTypeCode.ListWatchedFoldersRequest:
                    response = await _watchedFolderHandler.ListAsync(request, ct);
                    break;
                case MessageTypeCode.CreateWatchedFolderRequest:
                    response = await _watchedFolderHandler.CreateAsync(request, ct);
                    break;
                case MessageTypeCode.UpdateWatchedFolderRequest:
                    response = await _watchedFolderHandler.UpdateAsync(request, ct);
                    break;
                case MessageTypeCode.DeleteWatchedFolderRequest:
                    response = await _watchedFolderHandler.DeleteAsync(request, ct);
                    break;
                default:
                    ConnectionDebugLogger.Warn($"[DISPATCH] Unknown message type: {request.MessageType} from conn={request.ConnectionId}");
                    return CreateErrorResponse(request, 3, $"Unknown message type: {request.MessageType}");
            }

            ConnectionDebugLogger.Trace($"[DISPATCH] type={request.MessageType} completed in {sw.Elapsed.TotalMilliseconds:F1}ms (status={response.StatusCode})");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling {MessageType}", request.MessageType);
            ConnectionDebugLogger.Error(ex, $"[DISPATCH] Error handling {request.MessageType} from conn={request.ConnectionId} after {sw.Elapsed.TotalMilliseconds:F0}ms");
            return CreateErrorResponse(request, 13, "Internal server error");
        }
    }

    private static Envelope CreateErrorResponse(Envelope request, int statusCode, string message)
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
