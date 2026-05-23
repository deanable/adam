# Conventions

## Code Style

- **C# 13** with `ImplicitUsings` and `Nullable` enabled across all projects
- **File-scoped namespaces** — all .cs files use top-level namespace declarations (no braces)
- **Primary constructors** — not yet used; traditional constructor syntax prevalent
- **Collection expressions** — used (`[]` for empty collections in model initializers)

## Naming

- **PascalCase** for types, methods, properties, public fields
- **camelCase** for parameters, local variables
- **Async suffix** on async methods (`SaveChangesAsync`, `HandleConnectionAsync`)
- **Handler suffix** for request processors (`AuthHandler`, `AssetHandler`)

## Patterns

### MVVM (CatalogBrowser)
- Views inherit from `Avalonia.Controls.UserControl`
- ViewModels are POCOs with properties
- DI container wires ViewModels in `App.axaml.cs`
- No observable property generators detected (manual INotifyPropertyChanged not yet analyzed)

### Request Handling (BrokerService)
- Each handler is a singleton registered in DI
- `ConnectionHandler` dispatches envelopes to specific handlers by message type
- Handlers return `Envelope` responses with correlation IDs

### EF Core Configuration
- Fluent API in `OnModelCreating` (not data annotations)
- Many-to-many with explicit join entity configuration (`AssetKeywords`, `AssetCategories`)
- Global query filter for soft delete: `.HasQueryFilter(x => !x.IsDeleted)`
- Seeded roles in `SeedData`

### Protobuf Serialization
- Manual field numbering (not protoc-generated)
- `IProtoSerializable` contract: `CalculateSize()`, `WriteTo(CodedOutputStream)`, `MergeFrom(CodedInputStream)`
- `ProtoHelper` utility for common serialization tasks

## Project References

```
Adam.CatalogBrowser ──→ Adam.Shared
Adam.BrokerService ──→ Adam.Shared
```

Both depend on `Adam.Shared` but not on each other.

## Anti-Patterns Detected

- **Hardcoded values in client**: `BrokerClient` initialized with `("localhost", 5000)` but server listens on 9100
- **Static mutable state**: `AuthHandler._signingKey` is static — shared across instances, mutated in constructor
- **Synchronous file logger setup**: `FileLogger` registration in `App.axaml.cs` not fully analyzed for async safety
