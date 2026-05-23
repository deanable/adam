# Structure

## Repository Layout

```
adam/
├── specs/001-digital-asset-management/
│   ├── spec.md                 # Feature specification
│   ├── plan.md                 # Implementation plan with phases A-G
│   ├── data-model.md           # Entity definitions
│   ├── contracts/api.md        # TCP+Protobuf protocol spec
│   └── quickstart.md           # Setup guide
├── src/
│   ├── Adam.Shared/
│   │   ├── Models/             # Domain entities (12 files)
│   │   ├── Data/               # AppDbContext, IFileService
│   │   ├── Contracts/            # Protobuf message types (6 files)
│   │   ├── Services/             # MetadataExtractor, Thumbnail, Checksum, Search
│   │   ├── Transport/            # TcpFrame (length-prefixed framing)
│   │   └── Validation/           # AssetValidator
│   ├── Adam.BrokerService/
│   │   ├── Handlers/             # 9 message handlers
│   │   ├── Transport/            # TcpListenerService, TcpListenerHostedService
│   │   ├── Data/                 # DbProviderConfig, PostgresProvider, SqlServerProvider
│   │   ├── Hosting/              # OS service installers (3 platforms)
│   │   ├── Services/             # DbMigrationService, FolderWatcherService
│   │   └── Configuration/        # DbProviderConfig
│   └── Adam.CatalogBrowser/
│       ├── Views/                # Avalonia views (9 .axaml.cs files)
│       ├── ViewModels/           # MVVM view models (9 files)
│       ├── Services/             # BrokerClient, AuthSession, ModeManager, etc.
│       ├── Controls/             # Custom Avalonia controls (7 files)
│       └── Models/               # UI state models
├── tests/
│   ├── Adam.Shared.Tests/
│   ├── Adam.BrokerService.Tests/
│   │   └── Integration/          # ConcurrentClientsTests, DbProviderMatrixTests
│   └── Adam.CatalogBrowser.Tests/
└── .planning/                    # GSD planning directory (being created)
```

## File Counts

| Project | .cs Files | .csproj | Key Categories |
|---------|-----------|---------|----------------|
| Adam.Shared | ~25 | 1 | Models, Data, Services, Contracts |
| Adam.BrokerService | ~20 | 1 | Handlers, Transport, Hosting |
| Adam.CatalogBrowser | ~35 | 1 | Views, ViewModels, Services, Controls |
| Tests | ~10 | 3 | Unit, Integration |

## Notable Patterns

- **No solution file (.sln)**: Projects reference each other via `ProjectReference` in .csproj
- **Handler-per-Message-Type**: Each protobuf message type has a dedicated handler in BrokerService
- **View-per-Screen**: Each major screen has a matching View (.axaml) and ViewModel
- **Custom Control Library**: Reusable controls (AssetTile, SearchableTreeView, TagEditor) live in Controls/
