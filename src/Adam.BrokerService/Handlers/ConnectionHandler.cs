using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
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
        try
        {
            return request.MessageType switch
            {
                MessageTypeCode.LoginRequest => await _authHandler.LoginAsync(request, ct),
                MessageTypeCode.ValidateTokenRequest => _authHandler.ValidateToken(request),
                MessageTypeCode.ListAssetsRequest => await _assetHandler.ListAssetsAsync(request, ct),
                MessageTypeCode.GetAssetRequest => await _assetHandler.GetAssetAsync(request, ct),
                MessageTypeCode.ListCollectionsRequest => await _collectionHandler.ListCollectionsAsync(request, ct),
                MessageTypeCode.UpdateAssetRequest => await _assetHandler.UpdateAssetAsync(request, ct),
                MessageTypeCode.GetChangesRequest => await _changeHandler.GetChangesAsync(request, ct),
                MessageTypeCode.ListUsersRequest => await _userHandler.ListUsersAsync(request, ct),
                MessageTypeCode.ListRolesRequest => await _userHandler.ListRolesAsync(request, ct),
                MessageTypeCode.CreateUserRequest => await _userHandler.CreateUserAsync(request, ct),
                MessageTypeCode.UpdateUserRequest => await _userHandler.UpdateUserAsync(request, ct),
                MessageTypeCode.DeleteUserRequest => await _userHandler.DeleteUserAsync(request, ct),
                MessageTypeCode.ListAuditLogsRequest => await _auditLogHandler.ListAuditLogsAsync(request, ct),
                MessageTypeCode.GetServiceStatusRequest => await _statusHandler.GetStatusAsync(request, ct),
                MessageTypeCode.ListFoldersRequest => await _sidebarHandler.ListFoldersAsync(request, ct),
                MessageTypeCode.ListKeywordsRequest => await _sidebarHandler.ListKeywordsAsync(request, ct),
                MessageTypeCode.ListMediaFormatCountsRequest => await _sidebarHandler.ListMediaFormatCountsAsync(request, ct),
                MessageTypeCode.ListMetadataCategoriesRequest => await _sidebarHandler.ListMetadataCategoriesAsync(request, ct),
                MessageTypeCode.ListDateTakenTreeRequest => await _sidebarHandler.ListDateTakenTreeAsync(request, ct),
                MessageTypeCode.ListWatchedFoldersRequest => await _watchedFolderHandler.ListAsync(request, ct),
                MessageTypeCode.CreateWatchedFolderRequest => await _watchedFolderHandler.CreateAsync(request, ct),
                MessageTypeCode.UpdateWatchedFolderRequest => await _watchedFolderHandler.UpdateAsync(request, ct),
                MessageTypeCode.DeleteWatchedFolderRequest => await _watchedFolderHandler.DeleteAsync(request, ct),
                _ => CreateErrorResponse(request, 3, $"Unknown message type: {request.MessageType}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling {MessageType}", request.MessageType);
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
