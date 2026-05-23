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

    // Collections (30-39)
    ListCollectionsRequest = 30,
    ListCollectionsResponse = 31,
    CreateCollectionRequest = 32,
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

    // Status / Misc (90-99)
    GetServiceStatusRequest = 90,
    GetServiceStatusResponse = 91,
    NoData = 98,
    GeneralError = 99
}
