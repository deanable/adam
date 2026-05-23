# Architecture

## Overview

Adam is a dual-mode digital asset management system:
- **Standalone Mode**: Self-contained desktop app with direct SQLite access (no external service)
- **Multi-User Mode**: Catalog browser connects over TCP to a broker service backed by PostgreSQL/SQL Server

## Project Boundaries

```
┌─────────────────────────────────────────────────────────────┐
│                    Adam.CatalogBrowser                       │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │ Views       │  │ ViewModels  │  │ Services            │  │
│  │ (Avalonia)  │  │ (MVVM)      │  │ BrokerClient        │  │
│  └─────────────┘  └─────────────┘  │ AuthSession         │  │
│                                     │ ModeManager         │  │
│                                     │ ChangePoller        │  │
│                                     └─────────────────────┘  │
└──────────────────────┬────────────────────────────────────────┘
                       │ TCP + Protobuf
                       ▼
┌─────────────────────────────────────────────────────────────┐
│                    Adam.BrokerService                          │
│  ┌─────────────────┐  ┌─────────────┐  ┌──────────────────┐  │
│  │ Transport       │  │ Handlers    │  │ Hosting          │  │
│  │ TcpListener     │  │ Auth        │  │ Windows Service  │  │
│  │ (raw sockets)   │  │ Asset       │  │ macOS launchd    │  │
│  │                 │  │ Collection  │  │ Linux systemd    │  │
│  │                 │  │ User        │  └──────────────────┘  │
│  │                 │  │ Audit       │                        │
│  └─────────────────┘  └─────────────┘                        │
└──────────────────────┬────────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│                    Adam.Shared                               │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │ Models      │  │ Data        │  │ Services            │  │
│  │ DigitalAsset│  │ AppDbContext│  │ MetadataExtractor   │  │
│  │ Collection  │  │ (EF Core)   │  │ ThumbnailService    │  │
│  │ User        │  │             │  │ ChecksumService     │  │
│  │ Keyword     │  │ Contracts   │  │ SearchService       │  │
│  │ Role        │  │ (Protobuf)  │  │ IFileService        │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

## Data Flow

### Standalone Mode
1. User selects root folder on first launch
2. `FileIndexer` scans folder, `MetadataExtractorService` reads EXIF/IPTC/XMP
3. `AppDbContext` with SQLite provider stores assets and metadata
4. UI queries local `AppDbContext` directly via `Adam.Shared`

### Multi-User Mode
1. CatalogBrowser connects to BrokerService over TCP (port 9100)
2. `AuthHandler` validates credentials, issues JWT token
3. Browser sends protobuf-encoded requests (Asset, Collection, Search)
4. BrokerService handlers process requests against shared `AppDbContext`
5. `ChangePoller` on client polls for updates since last timestamp

## Key Architectural Decisions

- **Shared Library Pattern**: `Adam.Shared` contains all domain logic, models, and data access — both client and server reference it
- **Manual Protobuf**: No .proto compiler; custom `IProtoSerializable` interface with hand-written encode/decode
- **EF Core for Both Modes**: Same `AppDbContext` works with SQLite (standalone) and PostgreSQL/SQL Server (multi-user)
- **Last-Write-Wins Concurrency**: `Version` field on `DigitalAsset` incremented on save; no merge conflict resolution
- **Metadata Round-Trip**: Edits in client are written back to source files via XMP embedding (or sidecar for RAW)
