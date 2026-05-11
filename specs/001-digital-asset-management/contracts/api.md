# Service Contracts: adam Broker Service

## Communication Protocol

Multi-user mode uses **TCP sockets** with **Google.Protobuf** serialization. No web frameworks, no HTTP, no platform-specific APIs.

### Wire Format

```
[4-byte payload length (big-endian int32)] [protobuf-encoded Envelope message]
```

- Header: 4 bytes = payload length (network byte order)
- Body: protobuf serialization of `Envelope` message
- Authentication: JWT token in every request's `auth_token` field

### Envelope

Every message uses a generic envelope that wraps the specific payload:

```protobuf
message Envelope {
  string auth_token = 1;      // JWT, empty for LoginRequest
  string correlation_id = 2;  // Request-response matching
  string message_type = 3;    // Fully qualified type name
  bytes payload = 4;          // Protobuf-serialized payload
  int32 status_code = 5;      // 0=success, see error codes
  string error_message = 6;   // Populated on error
}
```

### Message Flow

1. Client opens TCP connection to broker service
2. Client sends `Envelope` with `message_type` set to the request type name
3. Server processes and responds with `Envelope` with matching `correlation_id`
4. Connection stays open for subsequent requests (keep-alive)

## Message Types

All messages use Google.Protobuf serialization (`Google.Protobuf` NuGet package, no gRPC dependency).

### Auth

| Type | Direction | Description |
|------|-----------|-------------|
| LoginRequest | Client → Server | Username + password |
| LoginResponse | Server → Client | JWT token + user profile |
| ValidateTokenRequest | Client → Server | Verify token validity |
| ValidateTokenResponse | Server → Client | Token status + user |

```protobuf
message LoginRequest {
  string username = 1;
  string password = 2;
}

message LoginResponse {
  string token = 1;
  int64 expires_at = 2;
  UserProfile user = 3;
}

message UserProfile {
  string id = 1;
  string username = 2;
  string role = 3;
}
```

### Assets

| Type | Direction | Description |
|------|-----------|-------------|
| ListAssetsRequest | Client → Server | Search/filter/paginate assets |
| ListAssetsResponse | Server → Client | Paginated asset summaries |
| GetAssetRequest | Client → Server | Single asset by ID |
| AssetDetailResponse | Server → Client | Full asset with metadata |
| CreateAssetRequest | Client → Server | Upload request with metadata + chunk |
| CreateAssetResponse | Server → Client | Created asset ID + duplicate status |
| UpdateAssetRequest | Client → Server | Metadata update with version |
| UpdateAssetResponse | Server → Client | New version number |
| DeleteAssetRequest | Client → Server | Soft-delete by ID |
| DeleteAssetResponse | Server → Client | Confirmation |
| GetChangesRequest | Client → Server | Timestamp-based change poll |
| GetChangesResponse | Server → Client | List of recent changes |

```protobuf
message ListAssetsRequest {
  string search = 1;
  string type = 2;
  string collection_id = 3;
  repeated string tags = 4;
  int32 page = 5;
  int32 page_size = 6;
  string sort_by = 7;
  string sort_dir = 8;
}

message ListAssetsResponse {
  repeated AssetSummary items = 1;
  int32 total_count = 2;
  int32 page = 3;
  int32 page_size = 4;
}

message AssetSummary {
  string id = 1;
  string file_name = 2;
  string mime_type = 3;
  int64 file_size = 4;
  string title = 5;
  string type = 6;
  string collection_id = 7;
  string uploaded_by = 8;
  int64 created_at = 9;
}

message GetAssetRequest {
  string id = 1;
}

message AssetDetail {
  string id = 1;
  string file_name = 2;
  string file_extension = 3;
  string mime_type = 4;
  int64 file_size = 5;
  string checksum_sha256 = 6;
  string title = 7;
  string description = 8;
  repeated string tags = 9;
  string type = 10;
  int32 width = 11;
  int32 height = 12;
  double duration = 13;
  string collection_id = 14;
  string collection_name = 15;
  string uploaded_by = 16;
  int32 version = 17;
  int64 created_at = 18;
  int64 modified_at = 19;
}

message CreateAssetRequest {
  string file_name = 1;
  bytes content = 2;
  string title = 3;
  string description = 4;
  repeated string tags = 5;
  string collection_id = 6;
}

message CreateAssetResponse {
  string id = 1;
  string checksum = 2;
  bool duplicate = 3;
  string existing_asset_id = 4;
}

message UpdateAssetRequest {
  string id = 1;
  string title = 2;
  string description = 3;
  repeated string tags = 4;
  string collection_id = 5;
  int32 expected_version = 6;
}

message UpdateAssetResponse {
  string id = 1;
  int32 new_version = 2;
  int64 modified_at = 3;
  bool conflict = 4;
}

message DeleteAssetRequest {
  string id = 1;
}

message DeleteAssetResponse {}

message GetChangesRequest {
  int64 since_timestamp = 1;
}

message GetChangesResponse {
  repeated ChangeEvent changes = 1;
}

message ChangeEvent {
  string entity_id = 1;
  string action = 2;
  int64 timestamp = 3;
}
```

### Collections

```protobuf
message ListCollectionsRequest {}

message ListCollectionsResponse {
  repeated CollectionNode items = 1;
}

message CollectionNode {
  string id = 1;
  string name = 2;
  string description = 3;
  string parent_id = 4;
  int32 asset_count = 5;
  repeated CollectionNode children = 6;
}

message CreateCollectionRequest {
  string name = 1;
  string description = 2;
  string parent_id = 3;
}

message UpdateCollectionRequest {
  string id = 1;
  string name = 2;
  string description = 3;
}

message CollectionSummary {
  string id = 1;
  string name = 2;
  int32 asset_count = 3;
}

message DeleteCollectionRequest {
  string id = 1;
}

message DeleteCollectionResponse {}
```

### Users (Admin)

```protobuf
message ListUsersRequest {
  int32 page = 1;
  int32 page_size = 2;
}

message ListUsersResponse {
  repeated UserSummary items = 1;
  int32 total_count = 2;
}

message UserSummary {
  string id = 1;
  string username = 2;
  string email = 3;
  string role = 4;
  bool is_active = 5;
  int64 created_at = 6;
}

message CreateUserRequest {
  string username = 1;
  string email = 2;
  string password = 3;
  string role = 4;
}

message UpdateUserRequest {
  string id = 1;
  string email = 2;
  string role = 3;
  bool is_active = 4;
}

message DeleteUserRequest {
  string id = 1;
}

message DeleteUserResponse {}
```

### Audit Log (Admin)

```protobuf
message QueryAuditLogRequest {
  string user_id = 1;
  string action = 2;
  string entity_type = 3;
  int64 from_timestamp = 4;
  int64 to_timestamp = 5;
  int32 page = 6;
  int32 page_size = 7;
}

message QueryAuditLogResponse {
  repeated AuditEntry items = 1;
  int32 total_count = 2;
}

message AuditEntry {
  string id = 1;
  string user_id = 2;
  string username = 3;
  string action = 4;
  string entity_type = 5;
  string entity_id = 6;
  string details = 7;
  int64 timestamp = 8;
}
```

## Status Codes

Envelope status codes (parity with gRPC status codes for familiarity):

| Code | Name | Usage |
|------|------|-------|
| 0 | OK | Success |
| 16 | UNAUTHENTICATED | Missing or invalid JWT token |
| 7 | PERMISSION_DENIED | Authenticated but insufficient role |
| 5 | NOT_FOUND | Requested entity does not exist |
| 6 | ALREADY_EXISTS | Duplicate asset detected |
| 9 | FAILED_PRECONDITION | Version conflict on update |
| 3 | INVALID_ARGUMENT | Validation failure |
| 13 | INTERNAL | Unexpected server error |
