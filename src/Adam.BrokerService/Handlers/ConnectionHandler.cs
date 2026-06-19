using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

public sealed class ConnectionHandler : IConnectionHandler
{
    private readonly MessageDispatcher _dispatcher;

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
        CommentHandler commentHandler,
        SavedSearchHandler savedSearchHandler,
        SearchHistoryHandler searchHistoryHandler,
        SemanticSearchHandler semanticSearchHandler,
        SearchRankingHandler searchRankingHandler,
        FaceHandler faceHandler,
        PersonHandler personHandler,
        PreferenceHandler preferenceHandler,
        ILogger<ConnectionHandler> logger)
    {
        _dispatcher = new MessageDispatcher(
            new Dictionary<MessageTypeCode, Func<Envelope, CancellationToken, Task<Envelope>>>
            {
                // ── Auth ──────────────────────────────────────────────
                [MessageTypeCode.LoginRequest] = (req, ct) => authHandler.LoginAsync(req, ct),
                [MessageTypeCode.ValidateTokenRequest] = (req, _) => Task.FromResult(authHandler.ValidateToken(req)),

                // ── Asset CRUD ────────────────────────────────────────
                [MessageTypeCode.ListAssetsRequest] = (req, ct) => assetHandler.ListAssetsAsync(req, ct),
                [MessageTypeCode.GetAssetRequest] = (req, ct) => assetHandler.GetAssetAsync(req, ct),
                [MessageTypeCode.CreateAssetRequest] = (req, ct) => assetHandler.CreateAssetAsync(req, ct),
                [MessageTypeCode.UpdateAssetRequest] = (req, ct) => assetHandler.UpdateAssetAsync(req, ct),
                [MessageTypeCode.DeleteAssetRequest] = (req, ct) => assetHandler.DeleteAssetAsync(req, ct),
                [MessageTypeCode.RestoreAssetRequest] = (req, ct) => assetHandler.RestoreAssetAsync(req, ct),
                [MessageTypeCode.ListDeletedAssetsRequest] = (req, ct) => assetHandler.ListDeletedAssetsAsync(req, ct),
                [MessageTypeCode.PermanentDeleteAssetRequest] = (req, ct) => assetHandler.PermanentDeleteAssetAsync(req, ct),
                [MessageTypeCode.BulkPermanentDeleteAssetRequest] = (req, ct) => assetHandler.BulkPermanentDeleteAssetAsync(req, ct),

                // ── File streaming ────────────────────────────────────
                [MessageTypeCode.GetFileRequest] = (req, ct) => assetHandler.GetFileAsync(req, ct),
                [MessageTypeCode.GetFileChunkRequest] = (req, ct) => assetHandler.GetFileChunkAsync(req, ct),

                // ── Collections ───────────────────────────────────────
                [MessageTypeCode.CreateCollectionRequest] = (req, ct) => collectionHandler.CreateCollectionAsync(req, ct),
                [MessageTypeCode.ListCollectionsRequest] = (req, ct) => collectionHandler.ListCollectionsAsync(req, ct),
                [MessageTypeCode.UpdateCollectionRequest] = (req, ct) => collectionHandler.UpdateCollectionAsync(req, ct),
                [MessageTypeCode.DeleteCollectionRequest] = (req, ct) => collectionHandler.DeleteCollectionAsync(req, ct),
                [MessageTypeCode.ReorderCollectionAssetsRequest] = (req, ct) => collectionHandler.ReorderCollectionAssetsAsync(req, ct),
                [MessageTypeCode.RefreshSmartCollectionRequest] = (req, ct) => collectionHandler.RefreshSmartCollectionAsync(req, ct),

                // ── Change tracking ───────────────────────────────────
                [MessageTypeCode.GetChangesRequest] = (req, ct) => changeHandler.GetChangesAsync(req, ct),

                // ── User management ───────────────────────────────────
                [MessageTypeCode.ListUsersRequest] = (req, ct) => userHandler.ListUsersAsync(req, ct),
                [MessageTypeCode.ListRolesRequest] = (req, ct) => userHandler.ListRolesAsync(req, ct),
                [MessageTypeCode.CreateUserRequest] = (req, ct) => userHandler.CreateUserAsync(req, ct),
                [MessageTypeCode.UpdateUserRequest] = (req, ct) => userHandler.UpdateUserAsync(req, ct),
                [MessageTypeCode.DeleteUserRequest] = (req, ct) => userHandler.DeleteUserAsync(req, ct),

                // ── Audit log ─────────────────────────────────────────
                [MessageTypeCode.ListAuditLogsRequest] = (req, ct) => auditLogHandler.ListAuditLogsAsync(req, ct),

                // ── Service status ────────────────────────────────────
                [MessageTypeCode.GetServiceStatusRequest] = (req, ct) => statusHandler.GetStatusAsync(req, ct),
                [MessageTypeCode.StartServiceRequest] = (req, ct) => statusHandler.StartServiceAsync(req, ct),
                [MessageTypeCode.StopServiceRequest] = (req, ct) => statusHandler.StopServiceAsync(req, ct),

                // ── Sidebar / keywords / categories / folders ─────────
                [MessageTypeCode.ListFoldersRequest] = (req, ct) => sidebarHandler.ListFoldersAsync(req, ct),
                [MessageTypeCode.ListKeywordsRequest] = (req, ct) => sidebarHandler.ListKeywordsAsync(req, ct),
                [MessageTypeCode.CreateKeywordRequest] = (req, ct) => sidebarHandler.CreateKeywordAsync(req, ct),
                [MessageTypeCode.UpdateKeywordRequest] = (req, ct) => sidebarHandler.UpdateKeywordAsync(req, ct),
                [MessageTypeCode.DeleteKeywordRequest] = (req, ct) => sidebarHandler.DeleteKeywordAsync(req, ct),
                [MessageTypeCode.CreateCategoryRequest] = (req, ct) => sidebarHandler.CreateCategoryAsync(req, ct),
                [MessageTypeCode.UpdateCategoryRequest] = (req, ct) => sidebarHandler.UpdateCategoryAsync(req, ct),
                [MessageTypeCode.DeleteCategoryRequest] = (req, ct) => sidebarHandler.DeleteCategoryAsync(req, ct),
                [MessageTypeCode.ListMediaFormatCountsRequest] = (req, ct) => sidebarHandler.ListMediaFormatCountsAsync(req, ct),
                [MessageTypeCode.ListMetadataCategoriesRequest] = (req, ct) => sidebarHandler.ListMetadataCategoriesAsync(req, ct),
                [MessageTypeCode.ListDateTakenTreeRequest] = (req, ct) => sidebarHandler.ListDateTakenTreeAsync(req, ct),

                // ── Watched folders ───────────────────────────────────
                [MessageTypeCode.ListWatchedFoldersRequest] = (req, ct) => watchedFolderHandler.ListAsync(req, ct),
                [MessageTypeCode.CreateWatchedFolderRequest] = (req, ct) => watchedFolderHandler.CreateAsync(req, ct),
                [MessageTypeCode.UpdateWatchedFolderRequest] = (req, ct) => watchedFolderHandler.UpdateAsync(req, ct),
                [MessageTypeCode.DeleteWatchedFolderRequest] = (req, ct) => watchedFolderHandler.DeleteAsync(req, ct),

                // ── Comments ──────────────────────────────────────────
                [MessageTypeCode.ListCommentsRequest] = (req, ct) => commentHandler.ListCommentsAsync(req, ct),
                [MessageTypeCode.CreateCommentRequest] = (req, ct) => commentHandler.CreateCommentAsync(req, ct),
                [MessageTypeCode.UpdateCommentRequest] = (req, ct) => commentHandler.UpdateCommentAsync(req, ct),
                [MessageTypeCode.DeleteCommentRequest] = (req, ct) => commentHandler.DeleteCommentAsync(req, ct),

                // ── Saved searches ────────────────────────────────────
                [MessageTypeCode.CreateSavedSearchRequest] = (req, ct) => savedSearchHandler.CreateSavedSearchAsync(req, ct),
                [MessageTypeCode.ListSavedSearchesRequest] = (req, ct) => savedSearchHandler.ListSavedSearchesAsync(req, ct),
                [MessageTypeCode.UpdateSavedSearchRequest] = (req, ct) => savedSearchHandler.UpdateSavedSearchAsync(req, ct),
                [MessageTypeCode.DeleteSavedSearchRequest] = (req, ct) => savedSearchHandler.DeleteSavedSearchAsync(req, ct),
                [MessageTypeCode.PinSavedSearchRequest] = (req, ct) => savedSearchHandler.PinSavedSearchAsync(req, ct),

                // ── Search history ────────────────────────────────────
                [MessageTypeCode.RecordSearchHistoryRequest] = (req, ct) => searchHistoryHandler.RecordSearchHistoryAsync(req, ct),
                [MessageTypeCode.ListSearchHistoryRequest] = (req, ct) => searchHistoryHandler.ListSearchHistoryAsync(req, ct),
                [MessageTypeCode.ClearSearchHistoryRequest] = (req, ct) => searchHistoryHandler.ClearSearchHistoryAsync(req, ct),

                // ── Semantic search ───────────────────────────────────
                [MessageTypeCode.SemanticSearchRequest] = (req, ct) => semanticSearchHandler.SearchByTextAsync(req, ct),
                [MessageTypeCode.FindSimilarRequest] = (req, ct) => semanticSearchHandler.FindSimilarAsync(req, ct),
                [MessageTypeCode.RecomputeEmbeddingsRequest] = (req, ct) => semanticSearchHandler.RecomputeEmbeddingsAsync(req, ct),

                // ── Smart Search Ranking ────────────────────────────────
                [MessageTypeCode.LogSearchClickRequest] = (req, ct) => searchRankingHandler.LogClickAsync(req, ct),
                [MessageTypeCode.ReRankRequest] = (req, ct) => searchRankingHandler.ReRankAsync(req, ct),

                // ── Facial Recognition ────────────────────────────────────
                [MessageTypeCode.DetectFacesRequest] = (req, ct) => faceHandler.DetectFacesAsync(req, ct),
                [MessageTypeCode.ListPersonsRequest] = (req, ct) => personHandler.ListPersonsAsync(req, ct),
                [MessageTypeCode.NamePersonRequest] = (req, ct) => personHandler.NamePersonAsync(req, ct),
                [MessageTypeCode.MergePersonsRequest] = (req, ct) => personHandler.MergePersonsAsync(req, ct),
                [MessageTypeCode.DeletePersonRequest] = (req, ct) => personHandler.DeletePersonAsync(req, ct),

                // ── User Preferences ──────────────────────────────────────
                [MessageTypeCode.GetPreferencesRequest] = (req, ct) => preferenceHandler.GetPreferencesAsync(req, ct),
                [MessageTypeCode.SetPreferenceRequest] = (req, ct) => preferenceHandler.SetPreferenceAsync(req, ct),
                [MessageTypeCode.ResetPreferenceRequest] = (req, ct) => preferenceHandler.ResetPreferenceAsync(req, ct),
                [MessageTypeCode.ResetAllPreferencesRequest] = (req, ct) => preferenceHandler.ResetAllPreferencesAsync(req, ct),
            },
            logger);
    }

    public Task<Envelope> HandleAsync(Envelope request, CancellationToken ct = default)
        => _dispatcher.DispatchAsync(request, ct);
}
