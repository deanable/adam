# Data Model: Digital Asset Management System (adam)

**Phase 1** — Entity definitions, fields, relationships, state transitions, validation rules.

## Entities

### DigitalAsset

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK, auto-generated |
| FileName | string | Required, max 500 chars |
| FileExtension | string | Required, max 50 chars |
| MimeType | string | Required, max 255 chars |
| FileSize | long | Required, bytes |
| ChecksumSha256 | string | Required, fixed 64 chars hex |
| StoragePath | string | Required, relative path within storage root |
| Title | string | Required, max 200 chars |
| Description | string | Optional, max 2000 chars |
| Tags | string[] | Optional, stored as JSON |
| Type | AssetType | Enum: Image, Video, Document, Audio, Other |
| Width | int? | Nullable, images/video only (pixels) |
| Height | int? | Nullable, images/video only (pixels) |
| Duration | double? | Nullable, video/audio only (seconds) |
| CollectionId | Guid | FK → Collection |
| UploadedByUserId | Guid? | Nullable (null in standalone mode); FK → User |
| IsDeleted | bool | Soft-delete flag, default false |
| Version | int | Optimistic concurrency, starts at 1 |
| CreatedAt | DateTimeOffset | UTC, set on creation |
| ModifiedAt | DateTimeOffset | UTC, updated on change |

**Uniqueness**: ChecksumSha256 unique within non-deleted assets (duplicate detection).

### Collection

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK, auto-generated |
| Name | string | Required, max 200 chars, unique within parent |
| Description | string | Optional, max 1000 chars |
| ParentId | Guid? | Optional FK → Collection (self-referencing hierarchy) |
| CreatedAt | DateTimeOffset | UTC |
| ModifiedAt | DateTimeOffset | UTC |

**Uniqueness**: Name unique per parent level. Root collections have unique names.

### User (multi-user mode only)

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK, auto-generated |
| Username | string | Required, max 100 chars, unique, case-insensitive |
| Email | string | Required, max 255 chars, unique |
| PasswordHash | string | Required, PBKDF2 hash |
| RoleId | Guid | FK → Role |
| IsActive | bool | Default true |
| CreatedAt | DateTimeOffset | UTC |
| LastLoginAt | DateTimeOffset? | Nullable |

### Role

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK, auto-generated |
| Name | string | Required, unique: "Viewer", "Editor", "Administrator" |
| Permissions | string[] | JSON array of permission strings |

**Seeded roles**:

| Role | Permissions |
|------|-------------|
| Viewer | `asset:read`, `collection:read` |
| Editor | `asset:read`, `asset:create`, `asset:update`, `collection:read`, `collection:update` |
| Administrator | `asset:*`, `collection:*`, `user:*`, `role:*`, `audit:read` |

### AccessLog

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK, auto-generated |
| UserId | Guid | FK → User |
| Action | string | Required: "create", "update", "delete", "login", "search" |
| EntityType | string | Required: "Asset", "Collection", "User", "Session" |
| EntityId | Guid? | Nullable, ID of affected entity |
| Details | string | Optional, JSON payload of change details |
| IpAddress | string | Optional, client IP |
| Timestamp | DateTimeOffset | UTC, immutable |

### ModeConfiguration

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK, auto-generated |
| Mode | string | "Standalone" or "MultiUser" |
| DbProvider | string | "sqlite", "postgresql", "sqlserver" |
| ConnectionString | string | Encrypted, connection string for the selected provider |
| ServiceEndpoint | string | Optional, TCP endpoint for multi-user mode |
| ServiceInstalled | bool | Whether native service has been registered |
| LastModified | DateTimeOffset | UTC |

## Relationships

```
Collection ──1:N──→ DigitalAsset
Collection ──1:N──→ Collection (self-referencing parent)

User ──1:N──→ DigitalAsset (uploaded by)
User ──N:1──→ Role
User ──1:N──→ AccessLog
```

## State Transitions

### DigitalAsset Lifecycle

```
[Uploading] → [Active] → [Deleted] (soft-delete)
                 ↑
[Duplicate] ─────┘ (replaces version)
```

### Operational Mode Lifecycle

```
[Standalone] ←──→ [Multi-User]
     │                  │
     │            ┌─────┴─────┐
     │            │           │
  SQLite      PostgreSQL  SQL Server
```

## Validation Rules

- File size: Max 2 GB per asset (configurable)
- File type: Whitelist enforced (MIME type check + extension)
- Collection nesting: Max 10 levels deep
- Username: 3-100 chars, alphanumeric + underscores
- Password: Min 8 chars, complexity enforced via System.Security.Cryptography
- Email: Valid email format
- Tags: Max 20 per asset, 50 chars per tag
