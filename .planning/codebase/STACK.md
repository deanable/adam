# Stack

## Core Framework

- **.NET 10.0** (preview) — Target framework for all projects
- **C# 13** — Language version with implicit usings and nullable reference types enabled

## UI Framework

- **Avalonia UI 12.0.3** — Cross-platform XAML UI framework for desktop (Windows, macOS, Linux)
  - Avalonia.Desktop — Desktop integration
  - Avalonia.Themes.Fluent — Fluent design theme
  - Avalonia.Fonts.Inter — Inter font family

## Data Access

- **Entity Framework Core 10.0.0-preview.3.25171.6** — ORM with code-first migrations
  - Microsoft.EntityFrameworkCore.Sqlite — Standalone/local mode provider
  - Microsoft.EntityFrameworkCore.SqlServer — Enterprise multi-user provider
  - Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0-preview.3 — PostgreSQL provider

## Serialization & Transport

- **Google.Protobuf 3.30.2** — Binary serialization for TCP message payloads
  - Manual protobuf encoding/decoding (no .proto compiler/generator used)
  - Custom `IProtoSerializable` interface with `CodedOutputStream`/`CodedInputStream`

## Image & Metadata

- **MetadataExtractor 2.8.1** — EXIF, IPTC, XMP metadata extraction from image files
- **SixLabors.ImageSharp 3.1.12** — Cross-platform image processing (thumbnails, transforms)

## Security

- **System.IdentityModel.Tokens.Jwt 8.6.1** — JWT token generation and validation for multi-user auth

## Hosting & DI

- **Microsoft.Extensions.Hosting 10.0.7** — Generic host for broker service lifecycle
- **Microsoft.Extensions.DependencyInjection 10.0.7** — DI container (used in both broker and browser)
- **Microsoft.Extensions.Logging 10.0.0-preview.3.25171.5** — Structured logging with console and debug providers

## Build & Test

- **SDK-style projects** — Modern .csproj format
- **xUnit** — Test framework (inferred from test project structure)
- **InternalsVisibleTo** — Test project access to internals in CatalogBrowser
