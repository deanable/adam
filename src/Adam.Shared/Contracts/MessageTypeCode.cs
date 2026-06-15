namespace Adam.Shared.Contracts;

/// <summary>
/// Stable opcode enum for message routing. Values must never be reassigned.
/// </summary>
public enum MessageTypeCode : ushort
{
    // Auth (1-9)
    LoginRequest = 1,
    LoginResponse = 2,
    ValidateTokenRequest = 3,
    ValidateTokenResponse = 4,

    // Assets (10-29)
    ListAssetsRequest = 10,
    ListAssetsResponse = 11,
    GetAssetRequest = 12,
    AssetDetail = 13,
    CreateAssetRequest = 14,
    CreateAssetResponse = 15,
    UpdateAssetRequest = 16,
    UpdateAssetResponse = 17,
    DeleteAssetRequest = 18,
    DeleteAssetResponse = 19,
    GetChangesRequest = 20,
    GetChangesResponse = 21,
    GetFileRequest = 22,
    GetFileResponse = 23,
    GetFileChunkRequest = 24,
    GetFileChunkResponse = 25,
    RestoreAssetRequest = 26,
    RestoreAssetResponse = 27,
    ListDeletedAssetsRequest = 28,
    ListDeletedAssetsResponse = 29,

    // Collections (30-39)
    ListCollectionsRequest = 30,
    ListCollectionsResponse = 31,
    CreateCollectionRequest = 32,
    CreateCollectionResponse = 36,
    UpdateCollectionRequest = 33,
    DeleteCollectionRequest = 34,
    DeleteCollectionResponse = 35,

    // Users / Roles / Audit (40-59)
    ListUsersRequest = 40,
    ListUsersResponse = 41,
    GetUserRequest = 42,
    CreateUserRequest = 43,
    CreateUserResponse = 44,
    UpdateUserRequest = 45,
    DeleteUserRequest = 46,
    ListRolesRequest = 47,
    ListRolesResponse = 48,
    ListAuditLogsRequest = 49,
    ListAuditLogsResponse = 50,

    // Sidebar / Browse (60-69)
    ListFoldersRequest = 60,
    ListFoldersResponse = 61,
    ListKeywordsRequest = 62,
    ListKeywordsResponse = 63,
    ListMediaFormatCountsRequest = 64,
    ListMediaFormatCountsResponse = 65,
    ListMetadataCategoriesRequest = 66,
    ListMetadataCategoriesResponse = 67,
    ListDateTakenTreeRequest = 68,
    ListDateTakenTreeResponse = 69,

    // Sidebar CRUD (80-89)
    CreateKeywordRequest = 80,
    CreateKeywordResponse = 81,
    UpdateKeywordRequest = 82,
    DeleteKeywordRequest = 83,
    DeleteKeywordResponse = 84,
    CreateCategoryRequest = 85,
    CreateCategoryResponse = 86,
    UpdateCategoryRequest = 87,
    DeleteCategoryRequest = 88,
    DeleteCategoryResponse = 89,

    // Watched Folders (70-79)
    ListWatchedFoldersRequest = 70,
    ListWatchedFoldersResponse = 71,
    CreateWatchedFolderRequest = 72,
    CreateWatchedFolderResponse = 73,
    UpdateWatchedFolderRequest = 74,
    DeleteWatchedFolderRequest = 75,
    DeleteWatchedFolderResponse = 76,

    // Change Notifications (110-112)
    ChangeNotification = 110,
    SubscribeChangesRequest = 111,
    SubscribeChangesResponse = 112,

    // Comments (120-128)
    ListCommentsRequest = 120,
    ListCommentsResponse = 121,
    CreateCommentRequest = 122,
    CreateCommentResponse = 123,
    UpdateCommentRequest = 124,
    UpdateCommentResponse = 125,
    DeleteCommentRequest = 126,
    DeleteCommentResponse = 127,
    CommentNotification = 128,

    // Session management (115)
    SessionInvalidated = 115,

    // Status / Misc (90-99)
    GetServiceStatusRequest = 90,
    GetServiceStatusResponse = 91,
    StartServiceRequest = 92,
    StartServiceResponse = 93,
    StopServiceRequest = 94,
    StopServiceResponse = 95,
    PermanentDeleteAssetRequest = 96,
    PermanentDeleteAssetResponse = 97,
    BulkPermanentDeleteAssetRequest = 100,
    BulkPermanentDeleteAssetResponse = 101,
    NoData = 98,
    GeneralError = 99
}
