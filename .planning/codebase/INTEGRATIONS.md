# Integrations

## External Libraries & Services

### Image Metadata
- **MetadataExtractor** — Primary integration for reading EXIF, IPTC, and XMP metadata from image files (JPEG, TIFF, RAW, PNG, WebP)
- **ImageSharp** — Thumbnail generation, image transformations (rotate, flip), export to JPEG/TIFF

### Database Providers
- **SQLite** — Embedded database for standalone mode; zero external dependency
- **PostgreSQL** — Via Npgsql EF Core provider; production multi-user deployments
- **SQL Server** — Enterprise multi-user deployments via Microsoft.Data.SqlClient

### Authentication
- **JWT Bearer Tokens** — Stateless auth between CatalogBrowser and BrokerService over TCP
  - Claims: NameIdentifier (user ID), Name (username), Role (permission set)
  - Ephemeral signing key fallback if not configured (warning: tokens invalidated on restart)

### Transport Protocol
- **Raw TCP Sockets** — Custom length-prefixed framing: `[4-byte length][protobuf payload]`
  - Port: 9100 (BrokerService default)
  - Max connections: 50
  - Max requests per connection: 500

### OS Service Hosting
- **Windows** — Service Control Manager (SCM) registration via `WindowsServiceInstaller`
- **macOS** — launchd plist generation via `MacOsServiceInstaller`
- **Linux** — systemd unit generation via `LinuxServiceInstaller`

## Integration Patterns

- **Shared Contracts** — Message types (Auth, Asset, Collection, User, Status) live in `Adam.Shared` and are manually protobuf-serialized
- **Shared Data** — `AppDbContext`, domain models, and `IFileService` abstraction in `Adam.Shared`
- **Change Polling** — Client-side `ChangePoller` polls broker for catalog updates (not push-based)
