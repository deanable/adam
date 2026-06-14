# Adam Wire Protocol

**Version:** 1.0  
**Last Updated:** 2026-06-14  
**Transport:** Raw TCP with length-prefixed protobuf framing

---

## 1. Transport Layer

### 1.1 Connection

Clients establish a raw TCP connection to the broker service. The default port is `9100` (configurable via `Broker:Port` in `appsettings.json`).

### 1.2 Framing

All messages use a **4-byte length prefix** followed by the payload:

```
┌──────────────────────────────────────────────┐
│  4 bytes (big-endian int32)                  │  ← Payload length (network byte order)
├──────────────────────────────────────────────┤
│  N bytes (protobuf-encoded Envelope)         │  ← Serialized Envelope
└──────────────────────────────────────────────┘
```

- **Length prefix**: 4-byte signed integer in network byte order (big-endian) via `IPAddress.HostToNetworkOrder()`
- **Max payload size**: 256 MB (268,435,456 bytes)
- **Send timeout**: 30 seconds (`DefaultSendTimeoutMs`)
- **Receive timeout**: 5 minutes (`DefaultReceiveTimeoutMs`)

### 1.3 Half-Close

The client sends a full TCP close (FIN) when disconnecting. The broker detects this when `ReadAsync` returns 0 bytes.

---

## 2. Envelope Structure

Every message is wrapped in an `Envelope` (field numbers 1-7):

| Field # | Name | Type | Required | Description |
|---------|------|------|----------|-------------|
| 1 | `AuthToken` | `string` | For auth'd requests | JWT bearer token from login |
| 2 | `CorrelationId` | `string` | Yes | Unique request identifier (echoed in response) |
| 3 | `MessageType` | `int32` (enum) | Yes | Opcode — see §3 |
| 4 | `Payload` | `bytes` | Per message type | Serialized request/response protobuf |
| 5 | `StatusCode` | `int32` | In responses | 0 = success, non-zero = error code |
| 6 | `ErrorMessage` | `string` | On error | Human-readable error description |
| 7 | `ClientIp` | `string` | Server-set | Client IP address (added by broker) |
| — | `ConnectionId` | — | Server-side only | Not serialized on wire |

### 2.1 Status Codes

| Code | Constant | Description |
|------|----------|-------------|
| 0 | `Success` | Operation completed successfully |
| 3 | `UnknownMessageType` | Unhandled opcode |
| 5 | `NotFound` | Entity not found (asset, user, etc.) |
| 6 | `Conflict` | Duplicate entity |
| 7 | `Forbidden` | Insufficient permissions or auth failure |
| 8 | `BadRequest` | Null, empty, or malformed payload |
| 13 | `InternalError` | Server-side exception |
| 14 | `InvalidArgument` | Invalid request parameter |
| 16 | `AuthDenied` | Rate limited or credential rejection |

---

## 3. Message Type Codes (Opcodes)

All opcodes are `ushort` values, never reassigned.

### 3.1 Auth (1-9)

| Opcode | Name | Direction | Payload (Request →) | Payload (← Response) |
|--------|------|-----------|---------------------|----------------------|
| 1 | `LoginRequest` | C→S | `LoginRequest` | |
| 2 | `LoginResponse` | S→C | | `LoginResponse` |
| 3 | `ValidateTokenRequest` | C→S | `ValidateTokenRequest` | |
| 4 | `ValidateTokenResponse` | S→C | | `ValidateTokenResponse` |

### 3.2 Assets (10-29)

| Opcode | Name | Direction | Request Payload | Response Payload |
|--------|------|-----------|-----------------|------------------|
| 10 | `ListAssetsRequest` | C→S | `ListAssetsRequest` | |
| 11 | `ListAssetsResponse` | S→C | | `ListAssetsResponse` |
| 12 | `GetAssetRequest` | C→S | `GetAssetRequest` | |
| 13 | `AssetDetail` | S→C | | `AssetDetail` |
| 14 | `CreateAssetRequest` | C→S | `CreateAssetRequest` | |
| 15 | `CreateAssetResponse` | S→C | | `CreateAssetResponse` |
| 16 | `UpdateAssetRequest` | C→S | `UpdateAssetRequest` | |
| 17 | `UpdateAssetResponse` | S→C | | `UpdateAssetResponse` |
| 18 | `DeleteAssetRequest` | C→S | `DeleteAssetRequest` | |
| 19 | `DeleteAssetResponse` | S→C | | `DeleteAssetResponse` |
| 20 | `GetChangesRequest` | C→S | `GetChangesRequest` | |
| 21 | `GetChangesResponse` | S→C | | `GetChangesResponse` |
| 22 | `GetFileRequest` | C→S | `GetFileRequest` | |
| 23 | `GetFileResponse` | S→C | | `GetFileResponse` |
| 24 | `GetFileChunkRequest` | C→S | `GetFileChunkRequest` | |
| 25 | `GetFileChunkResponse` | S→C | | `GetFileChunkResponse` |
| 26 | `RestoreAssetRequest` | C→S | `RestoreAssetRequest` | |
| 27 | `RestoreAssetResponse` | S→C | | `RestoreAssetResponse` |
| 28 | `ListDeletedAssetsRequest` | C→S | `ListDeletedAssetsRequest` | |
| 29 | `ListDeletedAssetsResponse` | S→C | | `ListDeletedAssetsResponse` |

### 3.3 Collections (30-39)

| Opcode | Name | Direction | Request Payload | Response Payload |
|--------|------|-----------|-----------------|------------------|
| 30 | `ListCollectionsRequest` | C→S | (empty) | |
| 31 | `ListCollectionsResponse` | S→C | | `ListCollectionsResponse` |
| 32 | `CreateCollectionRequest` | C→S | `CreateCollectionRequest` | |
| 33 | `UpdateCollectionRequest` | C→S | `UpdateCollectionRequest` | |
| 34 | `DeleteCollectionRequest` | C→S | `DeleteCollectionRequest` | |
| 35 | `DeleteCollectionResponse` | S→C | | `DeleteCollectionResponse` |
| 36 | `CreateCollectionResponse` | S→C | | `CreateCollectionResponse` |

### 3.4 Users / Roles / Audit (40-59)

| Opcode | Name | Direction | Request Payload | Response Payload |
|--------|------|-----------|-----------------|------------------|
| 40 | `ListUsersRequest` | C→S | (empty) | |
| 41 | `ListUsersResponse` | S→C | | `ListUsersResponse` |
| 42 | `GetUserRequest` | C→S | `GetUserRequest` | |
| 43 | `CreateUserRequest` | C→S | `CreateUserRequest` | |
| 44 | `CreateUserResponse` | S→C | | `CreateUserResponse` |
| 45 | `UpdateUserRequest` | C→S | `UpdateUserRequest` | |
| 46 | `DeleteUserRequest` | C→S | `DeleteUserRequest` | |
| 47 | `ListRolesRequest` | C→S | (empty) | |
| 48 | `ListRolesResponse` | S→C | | `ListRolesResponse` |
| 49 | `ListAuditLogsRequest` | C→S | `ListAuditLogsRequest` | |
| 50 | `ListAuditLogsResponse` | S→C | | `ListAuditLogsResponse` |

### 3.5 Sidebar / Browse (60-69)

| Opcode | Name | Direction | Request Payload | Response Payload |
|--------|------|-----------|-----------------|------------------|
| 60 | `ListFoldersRequest` | C→S | (empty) | |
| 61 | `ListFoldersResponse` | S→C | | `ListFoldersResponse` |
| 62 | `ListKeywordsRequest` | C→S | (empty) | |
| 63 | `ListKeywordsResponse` | S→C | | `ListKeywordsResponse` |
| 64 | `ListMediaFormatCountsRequest` | C→S | (empty) | |
| 65 | `ListMediaFormatCountsResponse` | S→C | | `ListMediaFormatCountsResponse` |
| 66 | `ListMetadataCategoriesRequest` | C→S | (empty) | |
| 67 | `ListMetadataCategoriesResponse` | S→C | | `ListMetadataCategoriesResponse` |
| 68 | `ListDateTakenTreeRequest` | C→S | (empty) | |
| 69 | `ListDateTakenTreeResponse` | S→C | | `ListDateTakenTreeResponse` |

### 3.6 Sidebar CRUD (80-89)

| Opcode | Name | Direction | Request Payload | Response Payload |
|--------|------|-----------|-----------------|------------------|
| 80 | `CreateKeywordRequest` | C→S | `CreateKeywordRequest` | |
| 81 | `CreateKeywordResponse` | S→C | | `CreateKeywordResponse` |
| 82 | `UpdateKeywordRequest` | C→S | `UpdateKeywordRequest` | |
| 83 | `DeleteKeywordRequest` | C→S | `DeleteKeywordRequest` | |
| 84 | `DeleteKeywordResponse` | S→C | | `DeleteKeywordResponse` |
| 85 | `CreateCategoryRequest` | C→S | `CreateCategoryRequest` | |
| 86 | `CreateCategoryResponse` | S→C | | `CreateCategoryResponse` |
| 87 | `UpdateCategoryRequest` | C→S | `UpdateCategoryRequest` | |
| 88 | `DeleteCategoryRequest` | C→S | `DeleteCategoryRequest` | |
| 89 | `DeleteCategoryResponse` | S→C | | `DeleteCategoryResponse` |

### 3.7 Watched Folders (70-79)

| Opcode | Name | Direction | Request Payload | Response Payload |
|--------|------|-----------|-----------------|------------------|
| 70 | `ListWatchedFoldersRequest` | C→S | (empty) | |
| 71 | `ListWatchedFoldersResponse` | S→C | | `ListWatchedFoldersResponse` |
| 72 | `CreateWatchedFolderRequest` | C→S | `CreateWatchedFolderRequest` | |
| 73 | `CreateWatchedFolderResponse` | S→C | | `CreateWatchedFolderResponse` |
| 74 | `UpdateWatchedFolderRequest` | C→S | `UpdateWatchedFolderRequest` | |
| 75 | `DeleteWatchedFolderRequest` | C→S | `DeleteWatchedFolderRequest` | |
| 76 | `DeleteWatchedFolderResponse` | S→C | | `DeleteWatchedFolderResponse` |

### 3.8 Status / Misc (90-101)

| Opcode | Name | Direction | Request Payload | Response Payload |
|--------|------|-----------|-----------------|------------------|
| 90 | `GetServiceStatusRequest` | C→S | (empty) | |
| 91 | `GetServiceStatusResponse` | S→C | | `GetServiceStatusResponse` |
| 92 | `StartServiceRequest` | C→S | (empty) | |
| 93 | `StartServiceResponse` | S→C | | `StartServiceResponse` |
| 94 | `StopServiceRequest` | C→S | (empty) | |
| 95 | `StopServiceResponse` | S→C | | `StopServiceResponse` |
| 96 | `PermanentDeleteAssetRequest` | C→S | `PermanentDeleteAssetRequest` | |
| 97 | `PermanentDeleteAssetResponse` | S→C | | `PermanentDeleteAssetResponse` |
| 98 | `NoData` | — | — | Empty envelope (keepalive) |
| 99 | `GeneralError` | S→C | — | Error-only response |
| 100 | `BulkPermanentDeleteAssetRequest` | C→S | `BulkPermanentDeleteAssetRequest` | |
| 101 | `BulkPermanentDeleteAssetResponse` | S→C | | `BulkPermanentDeleteAssetResponse` |

### 3.9 Change Notifications (110-115)

| Opcode | Name | Direction | Payload |
|--------|------|-----------|---------|
| 110 | `ChangeNotification` | S→C (push) | `ChangeNotification` |
| 111 | `SubscribeChangesRequest` | C→S | (empty) |
| 112 | `SubscribeChangesResponse` | S→C | `SubscribeChangesResponse` |
| 115 | `SessionInvalidated` | S→C (push) | (empty) |

---

## 4. Message Catalog

### 4.1 Envelope

```protobuf
message Envelope {
  string auth_token = 1;
  string correlation_id = 2;
  int32 message_type = 3;   // MessageTypeCode enum value
  bytes payload = 4;
  int32 status_code = 5;
  string error_message = 6;
  string client_ip = 7;
}
```

### 4.2 Auth Messages

**LoginRequest** (fields 1-2):
| Field # | Name | Type | Description |
|---------|------|------|-------------|
| 1 | `Username` | `string` | User's login name |
| 2 | `Password` | `string` | User's password (plaintext over TLS) |

**LoginResponse** (fields 1-3):
| Field # | Name | Type | Description |
|---------|------|------|-------------|
| 1 | `Token` | `string` | JWT access token |
| 2 | `ExpiresAt` | `int64` | Token expiry (Unix seconds) |
| 3 | `User` | `UserProfile` | Current user profile |

**UserProfile** (fields 1-3):
| Field # | Name | Type | Description |
|---------|------|------|-------------|
| 1 | `Id` | `string` | User GUID |
| 2 | `Username` | `string` | Login name |
| 3 | `Role` | `string` | Role name (e.g. "Administrator") |

**ValidateTokenResponse** (fields 1-2):
| Field # | Name | Type | Description |
|---------|------|------|-------------|
| 1 | `IsValid` | `bool` | Whether the token is still valid |
| 2 | `User` | `UserProfile` | Updated user profile (if valid) |

### 4.3 Asset Messages

**ListAssetsRequest** (fields 1-13):
| Field # | Name | Type | Default | Description |
|---------|------|------|---------|-------------|
| 1 | `Search` | `string` | "" | Full-text search query |
| 2 | `Type` | `string` | "" | Filter by asset type |
| 3 | `CollectionId` | `string` | "" | Filter by collection GUID |
| 4 | `Tags` | `repeated string` | [] | Filter by tag names |
| 5 | `Page` | `int32` | 1 | Page number for pagination |
| 6 | `PageSize` | `int32` | 50 | Items per page |
| 7 | `SortBy` | `string` | "FileName" | Sort column |
| 8 | `SortDir` | `string` | "asc" | Sort direction |
| 9 | `FromDate` | `int64` | 0 | Date range start (Unix seconds) |
| 10 | `ToDate` | `int64` | 0 | Date range end (Unix seconds) |
| 11 | `FolderPath` | `string` | "" | Filter by folder path |
| 12 | `KeywordIds` | `repeated string` | [] | Filter by keyword GUIDs |
| 13 | `CategoryIds` | `repeated string` | [] | Filter by category GUIDs |

**ListAssetsResponse** (fields 1-4):
| Field # | Name | Type | Description |
|---------|------|------|-------------|
| 1 | `Items` | `repeated AssetSummary` | Asset list for current page |
| 2 | `TotalCount` | `int32` | Total matching assets |
| 3 | `Page` | `int32` | Current page number |
| 4 | `PageSize` | `int32` | Items per page |

**AssetSummary** (fields 1-13):
| Field # | Name | Type | Description |
|---------|------|------|-------------|
| 1 | `Id` | `string` | Asset GUID |
| 2 | `FileName` | `string` | File name with extension |
| 3 | `MimeType` | `string` | MIME type |
| 4 | `FileSize` | `int64` | Size in bytes |
| 5 | `Title` | `string` | Display title |
| 6-13 | Rating, Label, Flag, etc. | various | Metadata fields |

*(See `AssetMessages.cs` for full field definitions of all 20+ message types)*

### 4.4 Collection Messages

**CollectionNode** (fields 1-6):
| Field # | Name | Type | Description |
|---------|------|------|-------------|
| 1 | `Id` | `string` | Collection GUID |
| 2 | `Name` | `string` | Display name |
| 3 | `Description` | `string` | Optional description |
| 4 | `ParentId` | `string` | Parent collection GUID (empty if root) |
| 5 | `AssetCount` | `int32` | Direct asset count |
| 6 | `Children` | `repeated CollectionNode` | Nested child collections |

**CreateCollectionRequest** (fields 1-3):
| Field # | Name | Type | Description |
|---------|------|------|-------------|
| 1 | `Name` | `string` | Collection name |
| 2 | `Description` | `string` | Optional description |
| 3 | `ParentId` | `string` | Parent GUID (empty = root) |

## 5. Auth Flow

### 5.1 Login

```
Client                          Server
  │                                │
  │── LoginRequest (opcode 1) ────→│
  │                                │── Validate credentials
  │                                │── Generate JWT
  │←── LoginResponse (opcode 2) ───│
  │    StatusCode=0, Token, User   │
```

### 5.2 Token Validation

```
Client                          Server
  │                                │
  │── ValidateTokenRequest ───────→│
  │    (opcode 3, AuthToken set)   │── Decode JWT
  │                                │── Check DB for active status
  │                                │── Detect role changes
  │←── ValidateTokenResponse ──────│
  │    (opcode 4, IsValid, User)   │
```

### 5.3 Session Invalidation (Server Push)

When an admin deactivates a user or changes their role, the server sends:

```
Server → Client: SessionInvalidated (opcode 115)
```

The client should call `ValidateTokenRequest` to refresh the user profile. StatusCode 7 (Forbidden) is used when the account is deactivated.

## 6. Change Notifications

When an asset, keyword, category, or collection is created/updated/deleted, the server broadcasts:

```
Server → All Clients (except originator):
  Envelope(MessageType=110, Payload=ChangeNotification)
```

**ChangeNotification** (fields 1-4):
| Field # | Name | Type | Description |
|---------|------|------|-------------|
| 1 | `EntityId` | `string` | Changed entity GUID |
| 2 | `Action` | `string` | "created", "updated", or "deleted" |
| 3 | `Timestamp` | `int64` | Unix seconds of change |
| 4 | `ChangedByUserId` | `string` | User GUID who made the change |

## 7. Error Handling

All error responses follow this pattern:

```json
{
  "CorrelationId": "<echoed from request>",
  "MessageType": "<original request opcode>",
  "StatusCode": "<error code>",      // non-zero
  "ErrorMessage": "<human-readable>"
}
```

### 7.1 Handler-Level Guards

All mutation handlers validate:
1. `request.Payload != null` — returns StatusCode 8 (BadRequest) if null
2. Successful protobuf deserialization — returns StatusCode 8 (BadRequest) if malformed
3. Authorization permission check — returns StatusCode 7 (Forbidden) if denied
4. Entity existence — returns StatusCode 5 (NotFound) if missing

## 8. Implementation Notes

### 8.1 Serialization

- All types implement `IProtoSerializable` (manual protobuf encoding via `CodedOutputStream`)
- Field numbers are defined as `const int` at the top of each message class
- Strings are UTF-8 encoded
- Empty strings and zero values are omitted (conditionally written)
- Repeated fields use `WriteRepeatedField` helper for arrays

### 8.2 Connection Lifecycle

1. Client opens TCP connection to `host:port`
2. `ConnectionHandler.HandleAsync` dispatches based on `MessageType`
3. Client sends `LoginRequest` to authenticate
4. All subsequent requests include `AuthToken` in the envelope
5. Client may subscribe to change notifications via `SubscribeChangesRequest`
6. Client disconnects by closing the TCP connection

### 8.3 Timeouts

| Timeout | Value | Location |
|---------|-------|----------|
| Send timeout | 30s | `TcpFrame.DefaultSendTimeoutMs` |
| Receive timeout | 5 min | `TcpFrame.DefaultReceiveTimeoutMs` |
| Token expiry | Configurable (default 24h) | `Jwt:TokenExpiryHours` |
| Connection idle | TCP keepalive | OS-configured |

---

*Generated from source: `src/Adam.Shared/Contracts/`, `src/Adam.Shared/Transport/`, `src/Adam.BrokerService/Handlers/`*
