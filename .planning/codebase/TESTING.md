# Testing

## Test Projects

| Project | Type | Count | Notes |
|---------|------|-------|-------|
| Adam.Shared.Tests | Unit | ~2 | Minimal; `UnitTest1.cs` placeholder, `PhaseDTests.cs` |
| Adam.BrokerService.Tests | Unit + Integration | ~3 | `ConcurrentClientsTests`, `DbProviderMatrixTests` |
| Adam.CatalogBrowser.Tests | Unit | ~5 | ViewModel tests, control tests, service tests |

## Integration Tests

- **ConcurrentClientsTests** — Validates 10 simultaneous client connections to broker
- **DbProviderMatrixTests** — Tests against SQLite, PostgreSQL, SQL Server (likely using Testcontainers)

## Test Coverage Assessment

- **Low overall coverage**: Many core services (MetadataExtractor, ThumbnailService, SearchService) lack dedicated tests
- **Integration tests present** but unit test surface area is thin
- **No UI tests detected**: Avalonia.Headless not yet integrated despite being in spec
- **Placeholder tests**: Several `UnitTest1.cs` files indicate tests were scaffolded but not filled in

## Gaps

- [ ] Metadata extraction test matrix (EXIF, IPTC, XMP across file types)
- [ ] Thumbnail generation tests
- [ ] Checksum/deduplication tests
- [ ] Search service tests (full-text, filtering)
- [ ] Protobuf serialization round-trip tests
- [ ] TCP transport error handling tests
- [ ] Auth handler tests (login, token validation, expiry)
- [ ] Avalonia UI tests (Avalonia.Headless)
