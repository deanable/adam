# Data Model: Digital Asset Management System (adam)

**Phase 1** — Relational entity definitions, fields, relationships, state transitions, validation rules.

## Design Principles

- **Assets are indexed in-place; no file copying.** The database stores only metadata and paths. Source files remain in their original locations on disk. There is no managed "storage directory" that duplicates files. This avoids filling the user's disk and enables distributed multi-user access where the broker service and catalog browser may run on different machines accessing the same network share.
- **Descriptive metadata lives in normalized junction tables.** Keywords and categories are many-to-many relationships. This guarantees referential integrity, enables indexed search, and prevents data duplication at scale.
- **Single-valued metadata stays on the entity.** Title, description, and extracted technical metadata remain on `DigitalAsset` or `MetadataProfile` because they are 1:1 with the asset.
- **No comma-separated or JSON array storage for searchable collections.** The legacy `Tags` string array has been removed. All tag-like data is stored via the `Keyword` and `Category` relational tables.

## Entities

### DigitalAsset

Core asset record. Contains file-system metadata and user-editable descriptive fields.

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK, auto-generated |
| FileName | string | Required, max 500 chars |
| FileExtension | string | Required, max 50 chars |
| MimeType | string | Required, max 255 chars |
| FileSize | long | Required, bytes |
| ChecksumSha256 | string | Required, fixed 64 chars hex |
| StoragePath | string | Required, max 2000 chars. Absolute or root-relative path to the original file on disk. The system does not copy or move the file. |
| OriginalPath | string | Required, max 2000 chars. The path as discovered during ingest (may be absolute or relative). Used for duplicate detection and re-scan matching. |
| Title | string | Required, max 200 chars |
| Description | string | Optional, max 2000 chars |
| Type | AssetType | Enum: Image, Video, Document, Audio, Other |
| Width | int? | Nullable, images/video only (pixels) |
| Height | int? | Nullable, images/video only (pixels) |
| Duration | double? | Nullable, video/audio only (seconds) |
| CollectionId | Guid? | FK → Collection |
| UploadedByUserId | Guid? | Nullable (null in standalone mode); FK → User |
| IsDeleted | bool | Soft-delete flag, default false |
| Version | int | Optimistic concurrency, starts at 1 |
| CreatedAt | DateTimeOffset | UTC, set on creation |
| ModifiedAt | DateTimeOffset | UTC, updated on change |

**Uniqueness**: ChecksumSha256 unique within non-deleted assets (duplicate detection).

**Removed**: `Tags` (string array). Replaced by the `Keywords` many-to-many relationship.

### Keyword

Descriptive tag assigned to assets. Supports unlimited hierarchical nesting via `ParentId` (FR-009).

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK, auto-generated |
| Name | string | Required, max 200 chars |
| NormalizedName | string | Required, max 200 chars, unique |
| ParentId | Guid? | Optional FK → Keyword (self-referencing hierarchy) |
| UsageCount | int | Derived counter, maintained by ingest/delete |

**Normalization rules**: Lowercase, trimmed, collapsed whitespace.

**Hierarchy examples**:
- `Nature` (root)
  - `Birds` (ParentId = Nature)
    - `Eagle` (ParentId = Birds)

When a user filters by `Birds`, the query includes all assets tagged with `Birds` or any of its descendants.

**Junction table**: `AssetKeywords` (`DigitalAssetId`, `KeywordId`). Composite PK. Both columns are indexed individually for bidirectional query support.

### Category

Semantic bucket for organizing assets (e.g., "Portfolio", "2025 Trip", extracted IPTC category). Distinct from keywords because categories represent curated or embedded classifications, not descriptive terms.

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK, auto-generated |
| Name | string | Required, max 200 chars, unique |
| NormalizedName | string | Required, max 200 chars, unique |
| Description | string | Optional, max 1000 chars |
| ParentId | Guid? | Optional FK → Category (self-referencing hierarchy) |

**Junction table**: `AssetCategories` (`DigitalAssetId`, `CategoryId`). Composite PK. Indexed for bidirectional queries.

### Collection

User-curated grouping, independent of on-disk folder structure.

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK, auto-generated |
| Name | string | Required, max 200 chars, unique within parent |
| Description | string | Optional, max 1000 chars |
| ParentId | Guid? | Optional FK → Collection (self-referencing hierarchy) |
| CreatedAt | DateTimeOffset | UTC |
| ModifiedAt | DateTimeOffset | UTC |

**Uniqueness**: Name unique per parent level. Root collections have unique names.

### MetadataProfile

Extracted technical metadata from EXIF/IPTC/XMP. 1:1 with DigitalAsset. Contains **only** camera, lens, exposure, GPS, and rating data. Descriptive fields that were previously stored here (`Category`, `Creator`, `Copyright`, `City`, `State`, `Country`, `Headline`, `Description`, `Title`) remain conceptually part of the asset or move to relational tables.

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK, auto-generated |
| DigitalAssetId | Guid | FK → DigitalAsset, unique (1:1) |
| CameraMake | string? | Max 200 chars |
| CameraModel | string? | Max 200 chars |
| LensModel | string? | Max 200 chars |
| FocalLength | double? | Millimeters |
| Aperture | double? | F-number |
| ExposureTime | string? | Max 50 chars |
| Iso | int? | |
| Flash | bool? | |
| GpsLatitude | double? | |
| GpsLongitude | double? | |
| GpsAltitude | double? | |
| DateTaken | DateTime? | |
| Orientation | string? | Max 50 chars |
| Rating | int? | 0–5 stars |

**Removed from this table**: `Category` string (replaced by `Category` junction table). `Creator`, `Copyright`, `City`, `State`, `Country`, `Headline`, `Description`, `Title` are extracted from IPTC/XMP but stored on `DigitalAsset` (Title, Description) or kept as read-only extracted fields if the user wants to preserve them exactly as embedded. For this design, those fields remain on `DigitalAsset` as the editable source of truth.

### RatingInfo

Extended rating data beyond the 0–5 star field in MetadataProfile (color labels, flags).

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK, auto-generated |
| DigitalAssetId | Guid | FK → DigitalAsset, unique (1:1) |
| Label | string? | Max 50 chars (Red, Green, Blue, Yellow, Purple) |
| Flag | string? | Max 50 chars (Pick, Reject) |

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
| EntityType | string | Required: "Asset", "Collection", "User", "Session", "Keyword", "Category" |
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

DigitalAsset ──N:M──→ Keyword (via AssetKeywords)
DigitalAsset ──N:M──→ Category (via AssetCategories)
DigitalAsset ──1:1──→ MetadataProfile
DigitalAsset ──1:1──→ RatingInfo

Keyword ──1:N──→ Keyword (self-referencing parent)
Category ──1:N──→ Category (self-referencing parent)

User ──1:N──→ DigitalAsset (uploaded by)
User ──N:1──→ Role
User ──1:N──→ AccessLog
```

## State Transitions

### DigitalAsset Lifecycle

```
[Scanning] → [Active] → [Deleted] (soft-delete)
                ↑
[Duplicate] ────┘ (replaces version)
```

### Keyword Hierarchy Ingest Rules

When extracting embedded metadata, hierarchical strings (e.g., "Nature|Birds|Eagle" from XMP HierarchicalSubject) are parsed and the full parent chain is created:

1. Split by `|` or `>` delimiter.
2. For each level, create the keyword if `NormalizedName` does not exist.
3. Set `ParentId` for each level to its predecessor.
4. Associate **only the leaf keyword** (`Eagle`) with the asset.

Search by `Birds` returns assets tagged with `Birds` or any descendant (`Eagle`, `Hawk`, etc.).

## Validation Rules

- File size: Max 2 GB per asset (configurable)
- File type: Whitelist enforced (MIME type check + extension)
- Collection nesting: Max 10 levels deep
- Username: 3–100 chars, alphanumeric + underscores
- Password: Min 8 chars, complexity enforced via System.Security.Cryptography
- Email: Valid email format
- Keywords per asset: Max 20, 50 chars per keyword name
- Categories per asset: Max 10

## Indexing Strategy

### Keyword Search Performance

```sql
-- Unique lookup for deduplication during ingest
CREATE UNIQUE INDEX IX_Keywords_NormalizedName ON Keywords(NormalizedName);

-- Find all assets for a keyword (bidirectional junction query)
CREATE INDEX IX_AssetKeywords_KeywordId ON AssetKeywords(KeywordId);
CREATE INDEX IX_AssetKeywords_AssetId ON AssetKeywords(AssetId);

-- Category lookups
CREATE UNIQUE INDEX IX_Categories_NormalizedName ON Categories(NormalizedName);
CREATE INDEX IX_AssetCategories_CategoryId ON AssetCategories(CategoryId);
CREATE INDEX IX_AssetCategories_AssetId ON AssetCategories(AssetId);
```

These indexes make keyword and category filtering an **indexed seek** rather than a full table scan. At 100K assets with an average of 10 keywords each, the junction tables contain ~1M rows — well within the performance envelope of all three supported database providers.
