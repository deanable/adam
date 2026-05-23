# adam — Digital Asset Management System

## What This Is

A cross-platform digital asset management (DAM) desktop application called **adam** that helps users organize, browse, search, and edit metadata for large collections of digital assets (photos, RAW images, videos, documents). It operates in two modes: a self-contained standalone desktop app (SQLite, no external service) and a multi-user shared catalog connected to a broker service over TCP (PostgreSQL/SQL Server).

## Core Value

Users can browse, search, and manage digital assets with full metadata round-trip — extracting EXIF/IPTC/XMP on ingest and writing edits back to source files — across Windows, macOS, and Linux.

## Requirements

### Validated

(None yet — ship to validate)

### Active

- [ ] Standalone mode launches and provides full catalog functionality without any external service or network
- [ ] Multi-user mode connects catalog browser to broker service over TCP with JWT authentication
- [ ] System extracts EXIF, IPTC, and XMP metadata from every recognized file during ingest
- [ ] Catalog browser provides grid view, loupe view, and compare view with adjustable thumbnails
- [ ] System supports hierarchical keywords, star ratings, color labels, and flagging on assets
- [ ] System supports curated collections independent of disk folder structure
- [ ] Users can edit metadata in the client and changes are written back to source files (XMP embedding or sidecar for RAW)
- [ ] System supports basic image adjustments (rotate, flip) and export to JPEG/TIFF
- [ ] Full-text search across all metadata fields returns results within 2 seconds for 100K assets
- [ ] Broker service accepts 10+ simultaneous client connections with responses under 3 seconds
- [ ] Metadata changes propagate to all connected users within 5 seconds
- [ ] System detects duplicate files via SHA256 checksum during scan
- [ ] Database provider abstraction supports SQLite, PostgreSQL, and SQL Server via config only
- [ ] Admin panel provides mode toggle, database migration wizard, and native service deployment
- [ ] Role-based access control (Viewer, Editor, Administrator) in multi-user mode
- [ ] All operations are audit-logged with timestamp and user identity

### Out of Scope

- Lightroom Develop module (pixel-level RAW processing) — basic adjustments only (rotate, flip, export)
- Real-time chat or collaboration features — not core to DAM value
- Video posts or video editing — asset metadata support only, no video manipulation
- OAuth/social login for v1 — email/password authentication sufficient
- Mobile app — desktop-first (Windows, macOS, Linux)
- Manual file upload mechanism — all ingestion is folder-scan based by design
- Cloud-scale distribution — targets LAN and single-machine deployments
- ASP.NET dependency — explicitly excluded per architecture constraint

## Context

- **Technology**: C# 13 / .NET 10 preview, Avalonia UI 12, EF Core 10 preview, Google.Protobuf 3.30, MetadataExtractor 2.8, ImageSharp 3.1
- **Architecture**: Dual-mode (standalone SQLite vs multi-user TCP broker); shared `Adam.Shared` library contains all domain models, EF Core DbContext, and protobuf contracts
- **Transport**: Raw TCP sockets with length-prefixed protobuf framing (not HTTP/gRPC)
- **Metadata**: Full round-trip — extract on ingest, write back to source files on edit (XMP embedding for JPEG/TIFF/PNG/WebP, sidecar for RAW)
- **Supported formats**: JPEG, PNG, WebP, TIFF, camera RAW (CR2, NEF, ARW, DNG), video (MP4, MOV), documents (PDF, DOCX, TXT), audio (MP3, WAV)
- **Codebase state**: Brownfield — 3 projects exist with domain models, EF Core configuration, TCP transport layer, broker handlers, and Avalonia UI shell. See `.planning/codebase/` for full analysis.
- **Known issues**: Client hardcodes broker port 5000 but server listens on 9100; static mutable JWT signing key; EF Core 10 preview dependencies; limited test coverage

## Constraints

- **Cross-platform identical behavior**: UI and data logic must work identically on Windows 10+, macOS 13+, and Linux (Ubuntu 22.04+/Fedora)
- **Zero ASP.NET dependency**: No web stack; desktop app + raw TCP service only
- **DB provider swap via config only**: Switching between SQLite/PostgreSQL/SQL Server requires only configuration change, no code changes
- **Standalone zero external dependencies**: Standalone mode must work with only the executable and a folder of assets
- **TCP between browser and service**: Multi-user mode uses raw TCP (not HTTP/REST/gRPC) for all client-server communication
- **Performance**: Search <2s at 100K assets; 10 concurrent users <3s response; metadata write-back <5s
- **Scale**: Standalone up to 100K assets; multi-user up to 50 concurrent users

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Dual-mode architecture (standalone + multi-user) | Explicit user requirement; standalone provides immediate value without infrastructure | — Pending |
| Manual protobuf serialization (no protoc) | Avoid build-time code generation complexity; full control over wire format | — Pending |
| EF Core shared across both modes | Same domain model works with SQLite (standalone) and PostgreSQL/SQL Server (multi-user) | — Pending |
| MetadataExtractor + ImageSharp | Cross-platform, no native dependencies, supports all required formats | — Pending |
| XMP round-trip to source files | Catalog must never diverge from source files; critical for professional DAM | — Pending |
| Raw TCP sockets instead of gRPC/HTTP | Explicit constraint; avoids ASP.NET and keeps dependency surface minimal | — Pending |
| Avalonia over MAUI/Uno | Cross-platform desktop-first, mature ecosystem, no mobile requirement | — Pending |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd-complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-05-23 after initialization*
