# Adam — Next Steps

A comprehensive catalogue of ways to extend and improve **Adam**, the dual-mode (standalone SQLite + multi-user TCP broker) cross-platform digital asset management system.

> **Status baseline:** v1.0 shipped (2026-06-12). v2.0 phases 10–12 already planned (Sidebar CRUD, FTS5, Performance). This document looks *past* the planned work and maps the broader opportunity space — covering both **table-stakes DAM features** every serious system has, and **differentiators** that could make Adam stand out.

**Legend**
- **Effort:** 🟢 small (days) · 🟡 medium (1–2 weeks) · 🔴 large (multi-week / multi-phase)
- **Value:** ⭐ nice-to-have · ⭐⭐ strong · ⭐⭐⭐ high-leverage
- **Maturity tag:** `[table-stakes]` expected in any pro DAM · `[differentiator]` premium/advanced · `[moonshot]` novel, could define the product

Items already present in Adam are noted so the gap analysis is honest.

---

## Table of Contents

1. [Already Planned (v2.0)](#1-already-planned-v20--context-only)
2. [Ingestion & Import Workflow](#2-ingestion--import-workflow)
3. [Asset Viewing, Culling & Selection](#3-asset-viewing-culling--selection)
4. [Metadata, Keywording & Taxonomy](#4-metadata-keywording--taxonomy)
5. [Metadata Standards & Interoperability](#5-metadata-standards--interoperability)
6. [Search & Discovery](#6-search--discovery)
7. [AI & Machine Learning](#7-ai--machine-learning)
8. [Rights, Licensing, Governance & Compliance](#8-rights-licensing-governance--compliance)
9. [Renditions, Export & Output](#9-renditions-export--output)
10. [Versioning & Non-Destructive Edits](#10-versioning--non-destructive-edits)
11. [Collaboration & Workflow](#11-collaboration--workflow)
12. [Distribution, Sharing & Portals](#12-distribution-sharing--portals)
13. [Integrations & Extensibility](#13-integrations--extensibility)
14. [Analytics & Reporting](#14-analytics--reporting)
15. [Infrastructure & Architecture](#15-infrastructure--architecture)
16. [Data Integrity, Backup & Preservation](#16-data-integrity-backup--preservation)
17. [Security & Identity](#17-security--identity)
18. [Platform Reach](#18-platform-reach)
19. [Distribution, Quality & Developer Experience](#19-distribution-quality--developer-experience)
20. [Accessibility & Internationalization](#20-accessibility--internationalization)
21. [Format & Media-Type Support](#21-format--media-type-support)
22. [Moonshots — Features That Make Adam Stand Out](#22-moonshots--features-that-make-adam-stand-out)
23. [Recommended Sequencing](#23-recommended-sequencing)
24. [Open Questions / Decisions Needed](#24-open-questions--decisions-needed)
25. [Deep Dive: Expanding Standard-Schema Metadata in the UI](#25-deep-dive--expanding-standard-schema-metadata-in-the-ui)
26. [Dedicated Settings Tab (Absorbing Audit)](#26-dedicated-settings-tab-absorbing-audit)
27. [Persisting User Preferences & UI State to the Catalog](#27-persisting-user-preferences--ui-state-to-the-catalog)
28. [Metadata Workspace Rework](#28-metadata-workspace-rework)

---

## 1. Already Planned (v2.0 — context only)

Scoped in `.planning/phases/`; the immediate next steps. Listed for completeness — the rest of this document assumes these land.

| Phase | Scope |
|-------|-------|
| **10 — Sidebar CRUD** | Create/rename/delete for Collections, Keywords, Categories; inline edit; context menus; permission gating; broker handlers + real-time sync |
| **11 — FTS5 Search** | Provider-specific full-text search (SQLite FTS5 / Postgres tsvector / SQL Server CONTAINS); BM25 ranking; suggestions. *(Services already drafted in `Adam.Shared/Services/`.)* |
| **12 — Performance** | Thumbnail cache (`ThumbnailCache.cs` started), gallery virtualization, <3s cold start, bounded memory at 100K+ assets |

---

## 2. Ingestion & Import Workflow

The on-ramp. Today Adam ingests via drag-drop and watched folders with EXIF/IPTC/XMP extraction. The pro-grade gaps:

- **Import-from-card / device ingest** 🟡 ⭐⭐ `[table-stakes]` — detect camera cards / connected devices, show an import dialog, copy + catalog in one step. The canonical Lightroom/Photo Mechanic entry point.
- **Filename rename templates at import** 🟢 ⭐⭐ `[table-stakes]` — token-based renaming (`{date}_{seq}_{camera}_{originalname}`) with live preview. Critical for archival naming discipline.
- **Metadata presets/templates at import** 🟢 ⭐⭐⭐ `[table-stakes]` — apply copyright, creator, contact, usage rights, and a base keyword set to every imported asset automatically. Reuses your `MetadataWritebackService`.
- **Backup-on-import / dual-destination copy** 🟡 ⭐⭐ `[table-stakes]` — copy to a working drive *and* a backup location during ingest (cards get wiped; this prevents loss).
- **Ingest-time duplicate handling** 🟢 ⭐⭐ — you already have `ChecksumService` + `DuplicateDetector`; surface "skip / import anyway / add to existing" decisions in the import dialog.
- **Tethered capture** 🔴 ⭐ `[differentiator]` — shoot directly into Adam from a connected camera (studio workflow). High effort (camera SDKs), niche but signature for studio photographers.
- **Folder-watcher rules engine** 🟡 ⭐⭐⭐ `[differentiator]` — "files arriving in `/Client-A/` → apply keyword set X + copyright Y + route to collection Z." Builds directly on the existing `FolderWatcherHostedService`. (See also §7 automation.)
- **Ingest queue with pause/resume + progress** 🟢 ⭐⭐ — robust, cancellable batch ingest with per-file status, retry on failure (extends `BulkOperationQueue`).
- **Offline/source-volume awareness** 🟡 ⭐⭐ `[table-stakes]` — track which storage volume an asset lives on; show "offline" when a drive is disconnected but keep thumbnails/metadata browsable.

---

## 3. Asset Viewing, Culling & Selection

This is where photographers spend the most time, and where Adam has its largest deferred-feature gap (loupe/compare were cut from v1).

- **Loupe / detail view** 🟡 ⭐⭐⭐ `[table-stakes]` — full-resolution pan/zoom, fit/100%/zoom-to-point, scrubbable. *Deferred v1 (CATA-05); currently a placeholder.* The single highest-signal gap.
- **Compare view (A/B)** 🟡 ⭐⭐ `[table-stakes]` — two assets side-by-side with synchronized pan/zoom for picking the keeper. *Deferred v1 (CATA-06).*
- **Survey / N-up view** 🟡 ⭐⭐ — review 4–16 candidates at once, demote/promote within the set.
- **Filmstrip** 🟢 ⭐⭐ — horizontal strip beneath loupe for rapid sequential navigation.
- **Dedicated full-screen cull mode** 🟡 ⭐⭐⭐ `[differentiator]` — keyboard-driven (pick/reject/rate) with auto-advance and minimal chrome. Daily-driver feature; turns Adam into a culling tool, not just a browser.
- **Burst / bracket / stack grouping** 🟡 ⭐⭐ — auto-group by capture-time proximity or filename pattern; collapse stacks to the pick. (Pairs well with the AI best-of-burst picker in §22.)
- **Histogram + clipping overlay** 🟢 ⭐⭐ `[table-stakes]` — RGB/luminance histogram in the loupe; highlight/shadow clipping warnings.
- **EXIF/shooting-data overlay** 🟢 ⭐⭐ — toggleable exposure triangle + lens/camera readout over the image (you already capture this in `MetadataProfile`).
- **Before/after split** 🟢 ⭐ — for assets with adjustments applied.
- **Slideshow / presentation mode** 🟢 ⭐ `[table-stakes]` — full-screen timed playback with transitions; client-facing review.
- **Configurable grid overlays** 🟢 ⭐ — rule-of-thirds, crop guides, focus-peaking on the loupe.
- **Color-managed display** 🟡 ⭐⭐ `[table-stakes for pros]` — honor the monitor ICC profile and asset color space so previews are accurate (see §5 color management).

---

## 4. Metadata, Keywording & Taxonomy

Adam has hierarchical keywords/categories, collections, ratings, labels, flags, GPS, and XMP write-back. To reach pro parity:

> **See [§25](#25-deep-dive--expanding-standard-schema-metadata-in-the-ui) for a detailed, codebase-grounded implementation plan** to expand the standard-schema metadata exposed and editable in the Metadata Editor (the near-term, concrete slice of this section).


- **Metadata presets / templates** 🟢 ⭐⭐⭐ `[table-stakes]` — saved sets of metadata (creator, copyright, rights, location) applied in bulk or at import.
- **Batch metadata edit across selection** 🟢 ⭐⭐ `[table-stakes]` — edit a field once, write to N assets; "sync metadata" from one asset to many.
- **Controlled vocabulary / keyword catalog** 🟡 ⭐⭐⭐ `[differentiator]` — import/manage a master keyword list; restrict tagging to approved terms (essential for libraries, archives, brands).
- **Keyword synonyms & aliases** 🟡 ⭐⭐ — "car" ⇆ "automobile"; export synonyms so searches and downstream systems resolve them.
- **Keyword sets / quick-tag palettes** 🟢 ⭐⭐ — clickable palette of common keywords per shoot type (Photo Mechanic / Lightroom style).
- **Keyword suggestions** 🟢 ⭐⭐ — autocomplete + "recently used" + "related keywords." Pairs with AI suggestions (§7).
- **Custom metadata fields / schemas** 🟡 ⭐⭐⭐ `[table-stakes for enterprise]` — admin-defined fields (e.g., "Campaign", "Client", "Shoot ID") with types (text/date/list/number). The line between a photo cataloguer and a true enterprise DAM.
- **Metadata panel presets** 🟢 ⭐ — show/hide field groups; per-user layouts.
- **People / face tagging UI** 🔴 ⭐⭐ `[table-stakes for consumer, differentiator for pro]` — name regions on faces; browse by person. (Recognition engine in §7.)
- **Map module / geotagging UI** 🟡 ⭐⭐ — plot GPS assets on a map, drag-to-geotag untagged assets, reverse-geocode to place names. GPS already captured.
- **Hierarchical metadata inheritance** 🟢 ⭐ — child keyword inherits parent ("Animals > Mammals > Dog" applies all three on search).
- **Metadata conflict resolution** 🟡 ⭐⭐ — when file XMP and catalog disagree (edited externally), surface and let the user choose authority. (Critical given your existing XMP round-trip.)
- **Color-label & flag customization** 🟢 ⭐ — rename labels ("Approved/Reject/Hold"), define team-wide label meaning.

---

## 5. Metadata Standards & Interoperability

What separates a credible professional DAM from a toy. Adam already does EXIF/IPTC/XMP + SHA256; the standards roadmap:

- **Full IPTC Photo Metadata (Core + Extension)** 🟡 ⭐⭐⭐ `[table-stakes]` — complete the IPTC field set: creator contact info, rights, scene, subject codes, location (created vs. shown), artwork/object, model/property release status. The lingua franca of stock and editorial.
- **Metadata Working Group (MWG) compliance** 🟡 ⭐⭐ — write metadata so Lightroom/Bridge/Photo Mechanic read it back identically (reconcile EXIF/IPTC-IIM/XMP per MWG guidelines). Interop credibility.
- **Dublin Core mapping** 🟢 ⭐ — map to/from DC for library/archive interchange.
- **schema.org / `ImageObject` export** 🟢 ⭐ — emit structured data for any future web surface (SEO + machine readability).
- **C2PA / Content Credentials** 🔴 ⭐⭐⭐ `[differentiator → moonshot]` — read, verify, preserve, and (optionally) sign content provenance manifests. Discloses capture device, edit history, and AI involvement. This is *the* hot 2024–2026 trust topic; you already ship BouncyCastle for crypto, so signing is feasible. A standout for editorial/news/legal users.
- **PLUS licensing metadata** 🟡 ⭐⭐ `[differentiator]` — structured machine-readable licensing (see §8).
- **Sidecar fidelity** 🟢 ⭐⭐ — you write `.xmp` sidecars for RAW; ensure round-trip parity with Lightroom/Bridge sidecars so catalogs interop.
- **Catalog import from Lightroom / other DAMs** 🔴 ⭐⭐⭐ `[differentiator]` — read Lightroom catalog (`.lrcat` is SQLite!) to migrate keywords, ratings, collections, virtual copies. The single biggest switching-cost remover for the target audience.
- **Bulk metadata import/export (CSV/JSON/XML)** 🟡 ⭐⭐ `[table-stakes]` — map spreadsheet columns to fields for bulk cataloguing and migration out.
- **Embargo/expiration + rights dates in metadata** 🟢 ⭐⭐ — store and surface (see §8).

---

## 6. Search & Discovery

FTS5 (Phase 11) is the foundation. On top of it:

- **Saved searches / smart collections** 🟡 ⭐⭐⭐ `[table-stakes]` — persist a query as a live virtual collection that auto-populates as assets change. The natural Phase-11 follow-on.
- **Faceted / filtered search** 🟡 ⭐⭐⭐ `[table-stakes]` — composable facets (camera + lens + date range + rating + keyword + label) with live result counts. Adam has filters today; faceting with counts is the upgrade.
- **Boolean & field-scoped queries** 🟢 ⭐⭐ — `keyword:sunset AND rating:>=4 NOT label:reject`; advanced query bar.
- **Search within results / progressive refine** 🟢 ⭐ — narrow an existing result set.
- **Duplicate & near-duplicate detection** 🟡 ⭐⭐⭐ `[differentiator]` — you have exact-match SHA256; add **perceptual hashing** (pHash/dHash) to find resized/recompressed/cropped near-dupes and offer visual review + merge.
- **Visual similarity search** 🔴 ⭐⭐⭐ `[differentiator]` — "find more like this" via embeddings (see §7 / §22).
- **Reverse-image search (find-by-image)** 🟡 ⭐⭐ — drop in an external image, find matching/similar catalog assets.
- **Search history & recents** 🟢 ⭐.
- **Empty-state / discovery surfaces** 🟢 ⭐⭐ — "On this day," "Recently added," "Untagged," "Never rated," "Largest files," "Missing metadata" — guided entry points that also drive data hygiene.

---

## 7. AI & Machine Learning

Adam's biggest differentiation lever — it already ships a **local ONNX vision-language model (LiquidVision / LFM2-VL)** with a clean `ILiquidVisionAnalyzer` abstraction and a `ExecutionProviderKind` (CPU/CUDA/TensorRT) seam. Local-first AI is a genuine privacy/cost advantage over cloud DAMs.

- **GPU execution path** 🟡 ⭐⭐⭐ — wire CUDA/DirectML through the existing `ExecutionProviderKind` to make bulk tagging 10–50× faster. Low architectural risk, huge throughput win.
- **Semantic embeddings + vector search** 🔴 ⭐⭐⭐ `[differentiator]` — generate CLIP-style image/text embeddings locally; store vectors (sqlite-vec / pgvector / in-memory ANN); enable natural-language and "find similar" search. The single most impactful AI add.
- **AI auto-tagging confidence + review queue** 🟡 ⭐⭐⭐ — surface low-confidence tags for human confirmation instead of silently applying; "AI suggested, pending approval" state. Builds trust and data quality.
- **Face detection & people clustering** 🔴 ⭐⭐ `[table-stakes for consumer]` — on-device face embedding + clustering (privacy-preserving, no cloud). Name a cluster once, propagate. digiKam/Mylio-class feature.
- **Object / scene / landmark detection** 🟡 ⭐⭐ — structured tags ("beach," "mountain," "wedding"); complements the VLM caption.
- **Aesthetic & quality scoring** 🟡 ⭐⭐⭐ `[differentiator]` — score sharpness, exposure, composition, eyes-open; powers the AI culling assistant (§22) and "best of" surfacing.
- **AI-generated alt-text** 🟢 ⭐⭐ `[differentiator]` — your VLM already produces descriptions; emit them as accessibility alt-text into IPTC `AltTextAccessibility` (a real IPTC field) — accessibility + SEO in one.
- **AI-generated SEO/marketing metadata** 🟢 ⭐⭐ — titles, descriptions, keyword suggestions for e-commerce/marketing assets.
- **OCR for images & documents** 🟡 ⭐⭐ — extract embedded text into the FTS index (you already index PDF text via PdfPig — extend to scanned images/screenshots).
- **NSFW / content moderation flagging** 🟡 ⭐ — on-device classifier for safe-for-work gating in shared catalogs.
- **Smart crop to focal point** 🟢 ⭐⭐ — saliency-aware auto-crop for thumbnails and export renditions (better than center-crop).
- **AI background removal** 🟡 ⭐⭐ `[differentiator]` — on-device matting (e.g., U²-Net/MODNet ONNX) for e-commerce/product workflows.
- **AI upscaling** 🟡 ⭐ — ONNX super-resolution for low-res asset rescue.
- **Speech-to-text for video/audio** 🔴 ⭐⭐ `[differentiator]` — Whisper-class ONNX transcription → searchable transcripts, auto-chapters for video assets (you already do video preview via LibVLCSharp).
- **Conversational catalog assistant** 🔴 ⭐⭐⭐ `[moonshot]` — see §22.
- **Model management UI** 🟢 ⭐⭐ — let users pick/download models, see size/speed/accuracy tradeoffs, choose CPU/GPU. Extends the existing model-download progress wiring.

---

## 8. Rights, Licensing, Governance & Compliance

The capability gap between a "photo browser" and an "asset management system" used by organizations.

- **Usage rights & license fields** 🟡 ⭐⭐⭐ `[table-stakes for enterprise]` — per-asset license type, holder, terms, permitted-use, restrictions. Store in IPTC rights fields + custom schema.
- **PLUS (Picture Licensing Universal System)** 🟡 ⭐⭐ `[differentiator]` — structured, machine-readable licensing metadata standard.
- **Rights expiration & embargo** 🟡 ⭐⭐⭐ `[differentiator]` — track license end dates / embargo start; auto-flag or restrict expired assets; dashboard of "expiring in 30 days." Major value for brands/agencies.
- **Model & property releases** 🟡 ⭐⭐ `[table-stakes for commercial]` — attach release documents to assets, track release status (released / not / N/A) — required for commercial stock.
- **Watermarking** 🟡 ⭐⭐ `[table-stakes for distribution]` — apply visible watermarks on export/preview/share; configurable text/image/opacity/position. (Reuses ImageSharp.)
- **Copyright registration helpers** 🟢 ⭐ — batch-export manifests for copyright office submission.
- **Retention & disposition policies** 🟡 ⭐⭐ `[table-stakes for archives]` — auto-archive/delete after N years; legal hold. Pairs with audit log.
- **Content authenticity (C2PA)** — see §5; governance + provenance overlap.
- **Compliance audit exports** 🟢 ⭐⭐ — exportable, tamper-evident audit trail (you have `AccessLog`) for SOC2/ISO evidence.

---

## 9. Renditions, Export & Output

Adam exports JPEG/TIFF with quality/resize. Pro output is broader:

- **Export presets** 🟢 ⭐⭐⭐ `[table-stakes]` — named, reusable export configs (format, size, quality, color space, watermark, metadata policy, output folder, filename template). "Export → Web / Print / Client" in one click.
- **Format breadth on export** 🟡 ⭐⭐ — add PNG, WebP, AVIF, HEIC, PDF; per-format options.
- **On-the-fly renditions** 🟡 ⭐⭐ `[table-stakes for enterprise]` — generate sized/format variants on demand (thumbnail/preview/web/print) rather than only at ingest; cache them.
- **Metadata policy on export** 🟢 ⭐⭐ `[table-stakes]` — strip-all / keep-copyright-only / keep-all; GPS-stripping for privacy. (Important and currently absent.)
- **Watermark on export** 🟡 ⭐⭐ — see §8.
- **Web galleries / contact sheets / PDF proofs** 🟡 ⭐⭐ `[table-stakes]` — generate a shareable gallery, contact sheet, or client proof PDF from a selection.
- **Print layout / print module** 🔴 ⭐ — page layouts, cell sizing, print sharpening (Lightroom Print module analog).
- **Video transcoding & proxies** 🔴 ⭐⭐ `[differentiator]` — generate web-friendly proxies and format conversions for video assets.
- **Export to social / publish services** 🟡 ⭐ — direct publish to platforms or to a sync folder.
- **Batch rename / export-with-rename** 🟢 ⭐⭐ — token templates (shared with import naming).

---

## 10. Versioning & Non-Destructive Edits

Adam does basic rotate/flip + XMP writeback. Full RAW develop is explicitly out of scope — but version/variant management is achievable and expected:

- **Version history per asset** 🟡 ⭐⭐⭐ `[table-stakes for enterprise]` — track replacements/edits over time; restore a prior version; "who changed what when" (you have `Version` concurrency token + `AccessLog` to build on).
- **Virtual copies / variants** 🟡 ⭐⭐ — multiple metadata/crop treatments of one master without duplicating the file (Lightroom virtual copy).
- **Non-destructive adjustment stack** 🔴 ⭐ — store crop/rotate/basic-tone as instructions, render on export. Keep narrow (you've ruled out a full develop module).
- **Crop & straighten (non-destructive)** 🟡 ⭐⭐ — high-value, bounded; aspect-ratio presets, store as metadata.
- **Edit-in external editor round-trip** 🟢 ⭐⭐ `[table-stakes]` — "Edit in Photoshop/Affinity/GIMP," re-import the result as a version/stack member. Cheap, very useful, sidesteps building an editor.

---

## 11. Collaboration & Workflow

Adam has multi-user RBAC (Viewer/Editor/Admin), audit logging, and real-time change notifications — strong bones. To become a team workflow tool:

- **Approval / review workflow** 🔴 ⭐⭐⭐ `[differentiator]` — states (Draft → In Review → Approved → Published → Archived); assignable reviewers; transitions logged. Builds on `AccessLog` + change notifications.
- **Comments & threaded discussion** 🟡 ⭐⭐ `[table-stakes for collaboration]` — per-asset comments, @mentions, resolve threads.
- **Annotations / markup** 🔴 ⭐⭐ `[differentiator]` — draw/pin notes on an image ("crop here," "fix color") — Frame.io-style review.
- **Task / assignment tracking** 🟡 ⭐ — assign assets/collections to users with due dates and status.
- **Optimistic-concurrency conflict UI** 🟡 ⭐⭐ — last-write-wins is silent today; surface conflicts and offer choose/merge.
- **Real-time presence** 🟡 ⭐ — show who's connected and what they're viewing (change-notification infra exists).
- **Activity feed** 🟢 ⭐⭐ — human-readable stream from `AccessLog` ("Dean tagged 12 assets," "Sam approved Shoot-22").
- **Notifications** 🟡 ⭐⭐ — in-app + email/webhook on assignment, mention, approval, expiring rights.
- **Per-collection / per-asset permissions** 🔴 ⭐⭐⭐ `[differentiator]` — RBAC is role-global today; granular ACLs unlock client-segregated workflows (Agency: "Client A users see only Client A assets").

---

## 12. Distribution, Sharing & Portals

Currently everything lives inside the desktop client. Distribution is where DAMs create organizational value — and where Adam's no-ASP.NET constraint forces a decision (see §24).

- **Share links** 🔴 ⭐⭐⭐ `[table-stakes for modern DAM]` — generate a link to an asset/collection for external viewing/download, with expiry, password, and download permissions.
- **Brand / client portals** 🔴 ⭐⭐ `[differentiator]` — curated, branded public-facing collections for clients/partners to self-serve.
- **Download presets for recipients** 🟡 ⭐⭐ — let a share recipient pick "web JPEG / print TIFF / original" — server renders on the fly.
- **Embeddable assets / hotlinks** 🟡 ⭐ — stable URLs for CMS/web embedding (needs a delivery surface).
- **Collection export package** 🟢 ⭐⭐ — bundle a collection + metadata sidecars + manifest into a portable archive (works *without* a web surface; good interim step).
- **Guest / external-reviewer accounts** 🟡 ⭐⭐ — time-boxed, scoped logins for clients (pairs with §11 review workflow).

---

## 13. Integrations & Extensibility

- **Public API + webhooks** 🔴 ⭐⭐⭐ `[differentiator]` — let other systems query/ingest/get notified. `Adam.Shared` already centralizes the domain logic; a thin API surface could reuse it. Webhooks on asset events (created/approved/expiring).
- **Plugin / extension SDK** 🔴 ⭐⭐ `[differentiator]` — third-party importers, exporters, metadata enrichers, publish services. Define stable extension contracts (you already have clean service interfaces).
- **Adobe Creative Cloud connector** 🔴 ⭐⭐ — "open in / save back to Adam" panel for Photoshop/Lightroom/InDesign. High desirability for the target user, high effort.
- **Office / Google Workspace insert** 🟡 ⭐ — pull approved assets into docs/slides.
- **CMS / e-commerce / PIM connectors** 🔴 ⭐ — WordPress, Shopify, headless CMS asset feeds.
- **Cloud storage backends** 🔴 ⭐⭐ — S3 / Azure Blob / Google Cloud / SMB as asset + thumbnail stores for distributed teams.
- **CLI / scripting interface** 🟡 ⭐⭐⭐ `[differentiator]` — headless ingest/tag/search/export for automation and CI. Reuses `Adam.Shared` directly; low architectural risk; great power-user feature.
- **Zapier / Make / n8n style automation hooks** 🟡 ⭐ — via webhooks/API.

---

## 14. Analytics & Reporting

Adam logs activity (`AccessLog`) but surfaces little. Analytics drive both user value and roadmap prioritization:

- **Asset usage analytics** 🟡 ⭐⭐ `[table-stakes for enterprise]` — views, downloads, exports, shares per asset; "most/least used."
- **Catalog health dashboard** 🟢 ⭐⭐⭐ `[differentiator]` — counts by type/format, untagged %, unrated %, missing-metadata %, storage by collection, duplicate count, expiring rights. Drives data hygiene; cheap to build from existing data.
- **User activity reports** 🟢 ⭐⭐ — per-user contributions/actions (multi-user); from `AccessLog`.
- **Storage & growth reporting** 🟢 ⭐ — disk usage over time, largest assets, growth trend.
- **Ingest/processing reports** 🟢 ⭐ — throughput, failures, AI-tagging coverage.
- **Exportable reports (CSV/PDF)** 🟢 ⭐ — for management/compliance.

---

## 15. Infrastructure & Architecture

### 15.1 Dependency & Platform Risk
- **Track EF Core 10 / .NET 10 GA** 🟢 ⭐⭐⭐ — currently on **preview** packages (flagged in `CONCERNS.md`). Move to stable ASAP. Lowest effort, highest risk reduction.
- **Protobuf schema versioning** 🟡 ⭐⭐ — manual protobuf (`IProtoSerializable`) lacks versioning; field-number drift is a latent bug source. Add version negotiation or migrate to compiled `.proto`.

### 15.2 Broker Service Hardening
- **Folder-watcher batching/debouncing** 🟡 ⭐⭐ — `CONCERNS.md` flags no batching for high-volume FS events; a bulk copy floods the indexer. Add debounce + batch ingest.
- **Health & metrics endpoint** 🟡 ⭐⭐ — service health, connection count, queue depth, sync lag for monitoring.
- **Connection resilience telemetry** 🟢 ⭐ — surface reconnect/backoff state to the user (logic exists; visibility doesn't).
- **Background job queue + scheduler** 🟡 ⭐⭐ — durable queue for thumbnailing, AI tagging, transcoding, rendition generation with retry/priority.
- **Graceful client/broker version negotiation** 🟡 ⭐⭐ — handle new-broker/old-client mismatches on upgrade.
- **Horizontal read scaling** 🔴 ⭐ — read replicas / pooling for larger teams (currently LAN/single-machine).

### 15.3 Storage & Caching
- **Persistent (on-disk) thumbnail/preview cache** 🟡 ⭐⭐ — `ThumbnailCache` is in-memory (Phase 12); add a disk tier so cold starts don't re-decode. Lightroom "Smart Previews" analog.
- **Preview pyramid / multi-resolution proxies** 🟡 ⭐⭐ — pre-render small/medium/loupe-size previews; loupe view (§3) loads instantly without touching originals.
- **Configurable cache location & size limits** 🟢 ⭐.

---

## 16. Data Integrity, Backup & Preservation

Trust features — conspicuously thin today.

- **Catalog backup/restore** 🟡 ⭐⭐⭐ `[table-stakes]` — one-click export of DB + settings; scheduled backups (multi-user). Highest-trust quick win.
- **Catalog integrity check / repair** 🟡 ⭐⭐ — detect orphaned thumbnails, missing sources, broken checksums; offer relink/repair.
- **Missing-file detection & relink** 🟡 ⭐⭐ `[table-stakes]` — flag "offline" assets when sources move; folder-based relink.
- **Fixity / checksum verification sweep** 🟢 ⭐⭐ `[differentiator for archives]` — periodic SHA256 re-verification to detect bit-rot/silent corruption; report changed files. (You already compute SHA256.)
- **OAIS / archival packaging (BagIt)** 🔴 ⭐ `[differentiator for institutions]` — export assets + metadata as preservation-grade BagIt packages.
- **Catalog interchange / migration export** 🔴 ⭐ — full XMP-sidecar round-trip or structured export for migration in/out.

---

## 17. Security & Identity

- **TLS by default + cert management UX** 🟡 ⭐⭐ — TLS is optional today; make it default and smooth certificate provisioning in ServiceManager.
- **SSO / OIDC / SAML** 🔴 ⭐⭐ `[table-stakes for enterprise]` — explicitly out of v1, but enterprise multi-user will demand it. Tension with the no-ASP.NET constraint (§24).
- **MFA / TOTP** 🟡 ⭐ — optional second factor.
- **Password policy** 🟢 ⭐⭐ — configurable complexity, rotation, lockout (you have `LoginRateLimiter` + PBKDF2).
- **Secrets management** 🟢 ⭐⭐ — JWT secret is env-var based; integrate OS secret stores / vault.
- **Encryption at rest** 🟡 ⭐ — optional DB/thumbnail encryption for sensitive catalogs.
- **Audit log export + retention** 🟢 ⭐⭐ — exportable, tamper-evident trail for compliance.
- **Brute-force / anomaly dashboards** 🟢 ⭐ — surface `LoginRateLimiter` state to admins.

---

## 18. Platform Reach

- **Web companion (read-only browse/review)** 🔴 ⭐⭐ `[differentiator]` — biggest reach multiplier; reviewers/clients browse without installing. Collides with the no-ASP.NET constraint — a deliberate decision (§24).
- **Headless CLI** 🟡 ⭐⭐⭐ — see §13; automation surface at low risk.
- **Mobile review app** 🔴 ⭐ — Avalonia supports mobile; a review-only surface could ride the same codebase.
- **Browser extension / "send to Adam"** 🟢 ⭐ — capture web images into a catalog (needs API).

---

## 19. Distribution, Quality & Developer Experience

### 19.1 Reliability & Onboarding
- **In-app auto-update** 🟡 ⭐⭐⭐ — installers exist (MSI/DMG/DEB) but no auto-update; users must manually re-download. High UX leverage.
- **Crash / error reporting** 🟢 ⭐⭐ — opt-in telemetry (Sentry-style) to catch field crashes. Currently file logs only.
- **First-run onboarding + sample catalog** 🟢 ⭐⭐ — guided tour + demo assets to shorten time-to-value.
- **In-app help / searchable docs** 🟢 ⭐ — surface the existing user/admin guides contextually.

### 19.2 Testing & CI
- **Raise coverage on weak paths** 🟡 ⭐⭐ — `CONCERNS.md` flags thin coverage on metadata extraction & thumbnail generation; CatalogBrowser has 23 tests vs. 239 in Shared.
- **Containerize Docker-dependent tests in CI** 🟢 ⭐⭐ — 2 Postgres/SQL Server integration tests skip without Docker; run them in CI so the provider matrix is always validated.
- **Avalonia headless UI tests** 🟡 ⭐ — ViewModel→View smoke tests.
- **Performance regression benchmarks** 🟡 ⭐⭐ — codify Phase 12 targets (<3s cold start, 100K scroll) as automated benchmarks.
- **Cross-platform release smoke tests** 🟢 ⭐⭐ — automated install-and-launch per platform in `release.yml`.

### 19.3 Observability
- **In-app log viewer + structured logging** 🟢 ⭐ — you have `FileLogger`/`ConnectionDebugLogger`; add a viewer and consistent fields.
- **Opt-in usage analytics** 🟢 ⭐ — privacy-respecting feature-usage signal to prioritize roadmap.

---

## 20. Accessibility & Internationalization

- **Keyboard-complete navigation + screen-reader labels** 🟡 ⭐⭐ — audit Avalonia automation peers; shortcuts exist but full a11y likely incomplete.
- **High-contrast & theme options** 🟢 ⭐ — light/dark/high-contrast (Fluent theme already used).
- **Localization (i18n)** 🟡 ⭐ — externalize strings; resource-based localization.
- **Configurable font scaling / UI density** 🟢 ⭐.
- **AI alt-text for accessibility** — see §7; turns the VLM into an a11y asset.

---

## 21. Format & Media-Type Support

Breadth of formats is a credibility signal. Current stack handles common images, video preview (LibVLCSharp), PDF/Office text (PdfPig/NPOI), audio (TagLib).

- **RAW format breadth** 🟡 ⭐⭐⭐ `[table-stakes]` — robust decode/preview for CR2/CR3, NEF, ARW, DNG, RAF, ORF, etc. (use embedded JPEG previews where full decode is heavy). Photographers live here.
- **Modern image formats** 🟢 ⭐⭐ — HEIC/HEIF, AVIF, JPEG XL ingest + preview.
- **Design files** 🟡 ⭐⭐ `[table-stakes for creative teams]` — PSD, AI, INDD, Sketch, Figma exports: extract embedded preview + metadata even without full render.
- **Document assets** 🟢 ⭐⭐ — broaden Office/PDF preview + text extraction (partly present).
- **Video metadata + scene thumbnails** 🟡 ⭐⭐ — duration, codec, framerate; multi-frame thumbnail strip; (extends LibVLCSharp use).
- **Audio waveform + metadata** 🟢 ⭐ — waveform preview, ID3 (TagLib present).
- **3D / AR assets** 🔴 ⭐ `[differentiator]` — glTF, OBJ, USDZ preview for product/AR workflows.
- **Vector / SVG / fonts** 🟢 ⭐ — preview + metadata for brand-asset libraries.

---

## 22. Moonshots — Features That Make Adam Stand Out

High-ambition ideas that leverage Adam's **local-first AI** advantage and could define the product. These are bets, not commitments.

- **🌟 Conversational catalog assistant** `[moonshot]` — a local LLM (you already run ONNX inference) you can *talk to* about your catalog: *"Show me the best 10 sunset shots from the Italy trip that I haven't exported,"* *"Tag all untagged product shots,"* *"Which assets have expiring licenses?"* Natural-language → structured query + actions over your metadata and embeddings. Nobody in the local/desktop DAM space does this well. This is the headline feature.
- **🌟 AI culling assistant (best-of-burst)** `[moonshot]` — combine burst grouping (§3) + aesthetic/quality scoring (§7) to auto-pick the sharpest, best-composed, eyes-open frame from each burst and pre-flag the rest as rejects. Cuts the most tedious photographer task from hours to minutes. Privacy-preserving and offline — a real edge over cloud tools.
- **🌟 Privacy-first AI as a brand position** `[differentiator]` — market the entire AI stack as *100% on-device, no cloud, no subscription, your photos never leave your machine.* A genuine, defensible differentiator versus Adobe/Bynder/cloud DAMs — especially for legal, medical, journalistic, and security-conscious users.
- **Auto-story / smart-album generation** 🔴 ⭐⭐ — cluster by time + place + people + similarity into suggested albums ("Weekend in Rome — 47 photos") with an auto-picked cover. Apple/Google Photos "Memories" for pros, locally.
- **Knowledge-graph asset linking** 🔴 ⭐⭐ — connect assets to people, places, events, projects, and to *each other* (variants, derivatives, same-shoot). You already have a `.planning/graphs/` concept — extend graph thinking to the catalog itself. Enables "show everything from this campaign" across collections.
- **Visual timeline / "rediscovery" surface** 🟡 ⭐⭐ — a scrollable time/place river of the catalog with "on this day," surfacing forgotten assets. Drives re-engagement and data hygiene.
- **Semantic dedup & "near-twin" cleanup wizard** 🟡 ⭐⭐ — perceptual-hash + embedding clustering to find visual near-duplicates across the whole catalog; guided bulk-merge to reclaim storage. (digiKam does basic similarity; an AI-grade wizard would stand out.)
- **Auto-metadata enrichment pipeline** 🟡 ⭐⭐ — on ingest, run a configurable chain: VLM caption → object tags → aesthetic score → alt-text → suggested keywords → reverse-geocode → all written as *pending* suggestions for one-click acceptance.
- **Content-authenticity storyteller (C2PA)** 🔴 ⭐⭐ — read and *visualize* the provenance chain of an asset (captured on X, edited in Y, AI-touched or not) and let users sign their own exports with Content Credentials. A trust feature for the deepfake era; you already ship the crypto library.
- **Natural-language smart collections** 🟡 ⭐⭐ — saved searches authored in plain English, kept live by the embedding index ("candid black-and-white portraits, 4+ stars").
- **Offline-first sync mesh** 🔴 ⭐ `[moonshot]` — peer-to-peer catalog sync between a photographer's laptop and studio machine without a server (CRDT-style), so the standalone mode gets multi-device without the broker. A unique middle ground between standalone and full multi-user.
- **Voice annotations & voice tagging** 🟢 ⭐ — record a voice note on an asset (transcribed by local Whisper) — fast field/cull workflow.

---

## 23. Recommended Sequencing

A pragmatic ordering once v2.0 phases 10–12 land. Grouped by horizon, balancing user-visible value against effort and risk.

### Near-term — trust, polish, and the obvious gaps (high value / low–moderate effort)
1. **EF Core / .NET 10 GA migration** (§15.1) — retire the preview-dependency risk first.
2. **Catalog backup/restore + integrity check** (§16) — trust foundation.
3. **In-app auto-update** (§19.1) — close the distribution loop.
4. **Loupe view + histogram** (§3) — the most-requested deferred v1 feature.
5. **Saved searches / smart collections** (§6) — natural FTS5 follow-on.
6. **Metadata presets + batch metadata + export presets** (§4, §9) — workflow efficiency staples.
7. **Catalog health dashboard** (§14) — cheap, drives data hygiene, demos well.

### Mid-term — pro-workflow depth and AI differentiation
8. **Cull mode + compare/survey views + burst stacking** (§3) — turns Adam into a culling tool.
9. **GPU inference + semantic embeddings + visual similarity search** (§7) — unlocks the AI story.
10. **AI culling assistant (best-of-burst)** (§22) — flagship differentiator built on #8–#9.
11. **Perceptual-hash near-duplicate detection + cleanup wizard** (§6, §22).
12. **Full IPTC + MWG compliance + Lightroom catalog import** (§5) — interop credibility + switching-cost remover.
13. **Folder-watcher batching + rules engine** (§2, §15.2) — robustness + automation.
14. **Headless CLI** (§13) — automation surface at low risk.
15. **Map module + people/face clustering** (§4, §7).

### Longer-term — strategic bets that change the product's category
16. **Conversational catalog assistant** (§22) — the headline local-AI feature.
17. **Web review companion + share links** (§12, §18) — reach multiplier (needs the no-ASP.NET decision).
18. **Approval workflow + comments/annotations + per-collection permissions** (§11) — from "team tool" to "agency/enterprise tool."
19. **Public API + plugin SDK + webhooks** (§13) — ecosystem.
20. **Rights management + C2PA content credentials** (§5, §8) — trust + commercial/editorial credibility.
21. **Cloud storage backends** (§13) — distributed teams.

---

## 24. Open Questions / Decisions Needed

These shape large swaths of the roadmap and deserve explicit decisions before committing.

- **Web surface vs. no-ASP.NET constraint** — share links, brand portals, web review, SSO, and embeddable assets (§12, §17, §18) all collide with the explicit no-ASP.NET decision. This single constraint gates the highest-reach features. Worth a deliberate revisit: is a small read-only delivery service acceptable, or is desktop-only a hard line?
- **AI ambition & scope** — how far to invest in the vision/LLM stack? A tagging assistant (today) → semantic search → culling assistant → conversational assistant is a ladder of increasing ambition and increasing differentiation. Local-first AI may be Adam's strongest wedge — is it *the* strategy or a feature?
- **Target market ceiling** — current design targets LAN / single-machine photographer and small-team use. Moving toward agency/enterprise (per-collection ACLs, SSO, rights management, portals, analytics) is a different product than going deeper for solo/pro photographers (culling, RAW breadth, loupe, develop-adjacent). Both are valid; they pull the roadmap in different directions.
- **Editing scope** — full RAW develop is ruled out. But non-destructive crop/straighten + edit-in-external round-trip (§10) is a bounded middle ground. Where exactly is the line?
- **Cloud & multi-device** — is cloud storage / multi-region ever in scope, or is the P2P "sync mesh" (§22) a better fit for the local-first ethos?
- **Mobile** — desktop-first is stated. Is a review-only mobile/web surface ever desired, given Avalonia could share the codebase?

---

## 25. Deep Dive — Expanding Standard-Schema Metadata in the UI

A concrete, codebase-grounded plan to widen the metadata Adam reads, stores, edits, and writes back through the **standard schemas** (EXIF, IPTC IIM, IPTC Core/Extension for XMP, XMP Rights, Dublin Core). This is the near-term, implementable slice of §4 + §5.

### 25.1 Current State (as built)

**Storage** — `src/Adam.Shared/Models/MetadataProfile.cs` holds:
- *Camera/EXIF:* `CameraMake`, `CameraModel`, `LensModel`, `FocalLength`, `Aperture`, `ExposureTime`, `Iso`, `Flash`, `Orientation`, `DateTaken`
- *GPS:* `GpsLatitude`, `GpsLongitude`, `GpsAltitude`
- *Descriptive/Rights/Location:* `Title`, `Headline`, `Description`, `Creator`, `Copyright`, `UsageTerms`, `ContactInfo`, `City`, `State`, `Country`
- *Misc:* `Rating`

**Read path** — `MetadataExtractorService` maps a *subset*: EXIF IFD0/SubIFD (make/model/lens/focal/aperture/exposure/ISO/flash/orientation/date), GPS, IPTC (`ByLine`→Creator, `Copyright`, `Headline`, `City`, `State`, `Country`), and XMP (`dc:creator`, `dc:rights` only). Keywords/categories/title/caption flow through a separate `ExtractedTextMetadata` path.

**Edit path** — `MetadataEditorViewModel` + `MetadataEditorView.axaml`:
- **Editable:** Title, Description, Tags (keywords), Rating.
- **Read-only (display only):** Make, Model, Lens, Focal, Aperture, Exposure, ISO, Date Taken, Flash, GPS, Creator, Copyright, Headline.
- `SaveAsync` persists only `Title`, `Description`, keywords, and `profile.Rating`.

**Write path** — `MetadataWritebackService.BuildXmpPacket` emits `dc:creator`, `dc:title`, `dc:description`, `dc:rights`, `photoshop:Headline`, `xmp:Rating`, `xmpRights:UsageTerms`, a flattened `Iptc4xmpCore:Location` string, `dc:subject` (keywords), `photoshop:Label`, and `exif:GPSLatitude/Longitude`.

### 25.2 The Gaps

1. **Model fields exist but are invisible:** `UsageTerms`, `ContactInfo`, `City`, `State`, `Country`, `Orientation`, `GpsAltitude` are stored/extractable but **not shown in the editor at all**.
2. **Authoring fields are read-only:** `Creator`, `Copyright`, `Headline` are IPTC *authoring* fields a user should be able to edit, but the UI treats them as read-only camera data, and `SaveAsync` wouldn't persist them even if edited.
3. **Save doesn't write standard fields back:** the editor's `SaveAsync` neither persists the IPTC/rights/location profile fields nor invokes `MetadataWritebackService`, so edits never reach the file's XMP/sidecar.
4. **Schema coverage is shallow:** large parts of IPTC Core, all of IPTC Extension, most XMP Rights, and Dublin Core are neither parsed, stored, nor writable.

### 25.3 Proposed Field Set (organized by editor panel / schema)

> Legend: **E** = should be user-editable · **R** = read-only (from file). Schema column gives the canonical namespace/field for read + writeback fidelity.

**Panel A — Description & Content (E)** `[table-stakes]`
| Field | Schema |
|-------|--------|
| Title / Object Name | `dc:title` / IPTC `ObjectName` |
| Headline | `photoshop:Headline` |
| Description / Caption | `dc:description` / IPTC `Caption` |
| Caption Writer | `photoshop:CaptionWriter` |
| Keywords | `dc:subject` (flat) + `lr:hierarchicalSubject` |
| Instructions | `photoshop:Instructions` |
| Intellectual Genre | `Iptc4xmpCore:IntellectualGenre` |
| Scene Code / Subject Code | `Iptc4xmpCore:Scene` / `SubjectCode` |
| Urgency | `photoshop:Urgency` |

**Panel B — Creator & Contact (E)** `[table-stakes]`
| Field | Schema |
|-------|--------|
| Creator / By-line | `dc:creator` / IPTC `By-line` |
| Creator's Job Title | `photoshop:AuthorsPosition` |
| Credit Line | `photoshop:Credit` |
| Source | `photoshop:Source` |
| Creator Contact Info (address, city, region, postal, country, phone, email, website) | `Iptc4xmpCore:CreatorContactInfo` (structured) |

**Panel C — Rights & Licensing (E)** `[differentiator]`
| Field | Schema |
|-------|--------|
| Copyright Notice | `dc:rights` |
| Copyright Status (Marked) | `xmpRights:Marked` (bool) |
| Rights Usage Terms | `xmpRights:UsageTerms` |
| Web Statement of Rights | `xmpRights:WebStatement` |
| Copyright Owner | `plus:CopyrightOwner` |
| Licensor | `plus:Licensor` |
| Model Release Status / ID | `plus:ModelReleaseStatus` / `ModelReleaseID` |
| Property Release Status / ID | `plus:PropertyReleaseStatus` / `PropertyReleaseID` |
| **Digital Source Type** (AI-generation disclosure) | `Iptc4xmpExt:DigitalSourceType` |

**Panel D — Location (E)** `[table-stakes]`
| Field | Schema |
|-------|--------|
| Sublocation | `Iptc4xmpCore:Location` |
| City / State-Province / Country / ISO Country Code | `photoshop:City` / `State` / `Country` / `Iptc4xmpCore:CountryCode` |
| Location Created vs. Location Shown | `Iptc4xmpExt:LocationCreated` / `LocationShown` (structured) |

**Panel E — Camera / EXIF (R)** `[table-stakes]` — extend the existing read-only panel
| Field | Schema |
|-------|--------|
| (existing) Make, Model, Lens, Focal, Aperture, Exposure, ISO, Flash, Orientation | EXIF |
| Exposure Program / Metering Mode / White Balance | `exif:ExposureProgram` / `MeteringMode` / `WhiteBalance` |
| Exposure Bias / Max Aperture / Focal Length (35mm) | `exif:ExposureBiasValue` / `MaxApertureValue` / `FocalLengthIn35mmFilm` |
| Color Space | `exif:ColorSpace` |
| Software / Creator Tool | `xmp:CreatorTool` / EXIF `Software` |

**Panel F — Dates (mixed)** `[table-stakes]`
| Field | Schema |
|-------|--------|
| Date Taken / Original (R) | `exif:DateTimeOriginal` |
| Date Created (E) | `photoshop:DateCreated` / IPTC `DateCreated` |
| Date Modified (R) | `xmp:ModifyDate` |

**Panel G — GPS / Map (R, see §4 map module)**
| Field | Schema |
|-------|--------|
| Latitude / Longitude / Altitude | `exif:GPSLatitude` / `GPSLongitude` / `GPSAltitude` |
| Image Direction | `exif:GPSImgDirection` |
| Resolved place name (reverse-geocode) | derived |

**Panel H — Raw Metadata Viewer (R)** `[differentiator]`
- A collapsible "All metadata" expander that dumps every extracted EXIF/IPTC/XMP key→value pair (MetadataExtractor already enumerates these). Cheap power-user feature; invaluable for debugging and for fields not yet first-classed.

**Dublin Core export mapping** `[nice]` — map `dc:publisher`, `dc:contributor`, `dc:type`, `dc:format`, `dc:identifier`, `dc:language`, `dc:coverage` for interchange/export (no UI needed initially; write-only).

### 25.4 UI Changes (`MetadataEditorView.axaml` + ViewModel)

**Proposed layout** — replace today's flat two-column (editable left / read-only right) form with a single scrollable column of **collapsible schema panels**. Editable panels open by default; read-only/technical panels start collapsed:

```
┌─ Metadata Editor ──────────────────────────────────────────────┐
│ IMG_4821.CR2                                     [ Save Changes ]│
│ ─────────────────────────────────────────────────────────────  │
│ ▼ Description & Content                              (editable) │
│    Title          [ Sunset over the bay______________________ ] │
│    Headline       [ Golden hour, Amalfi Coast________________ ]  │
│    Description     ┌────────────────────────────────────────┐   │
│                    │ Wide view of the harbour at dusk…      │   │
│                    └────────────────────────────────────────┘   │
│    Caption Writer [ D. Kruger______ ]    Urgency  [ 5 ▼ ]       │
│    Keywords       [sunset ✕] [coast ✕] [italy ✕]  (+ add…)      │
│    Genre [ Feature ▼ ]  Scene [ 011900 ]  Subject [ 08000000 ]  │
│                                                                 │
│ ▼ Creator & Contact                                 (editable) │
│    Creator   [ Dean Kruger______ ]   Job Title [ Photographer ] │
│    Credit    [ Dean Kruger/Adam_ ]   Source    [ Adam Studios ] │
│    ▸ Contact info (address · city · region · postal · country   │
│                    · phone · email · website)                   │
│                                                                 │
│ ▼ Rights & Licensing                                (editable) │
│    Copyright [ © 2026 Dean Kruger ]   Status [✓] Copyrighted    │
│    Usage Terms ┌──────────────────────────────────────────┐    │
│                │ Editorial use only. No resale.           │    │
│                └──────────────────────────────────────────┘    │
│    Web Statement [ https://… ]                                  │
│    Model Release    [ Not Applicable ▼ ]   ID [ ________ ]      │
│    Property Release [ Unlimited ▼ ]        ID [ ________ ]      │
│    Digital Source   [ Original digital capture ▼ ]  ← AI flag   │
│                                                                 │
│ ▼ Location                                          (editable) │
│    Sublocation [ Marina___ ]   City [ Amalfi___ ]              │
│    State [ Salerno ]  Country [ Italy ]  ISO Code [ ITA ]       │
│    ▸ Location created     ▸ Location shown                       │
│                                                                 │
│ ▶ Dates                                            (mixed)     │
│ ▶ Camera / EXIF                                    (read-only) │
│ ▶ GPS / Map                                        (read-only) │
│ ▶ All Metadata (raw key→value dump)                (read-only) │
└─────────────────────────────────────────────────────────────────┘
```

- Implement panels as **grouped, collapsible `Expander` controls** (A–H above); remember expand/collapse per user.
- Keep the **editable vs. read-only** split: Panels A–D + Date Created are editable (gated by existing `CanEdit`); Panels E, G, parts of F, and H are read-only.
- Field-appropriate controls: multiline `TextBox` (description/instructions), date pickers (date created), **toggle** for Copyright Status, **dropdowns** for controlled values (Model/Property release status, Urgency, Digital Source Type).
- Per-field tooltip showing the **canonical schema name** (e.g. "`Iptc4xmpCore:CreatorContactInfo` → Email") for transparency.
- All editable fields participate in the existing **dirty/`SaveCommand`** flow.

### 25.5 Backend Work

1. **Model** (`MetadataProfile.cs`) — add the new properties (Panels A–D + new EXIF/date fields). Consider a small owned/structured type for `CreatorContactInfo` and `Location*` rather than flattened strings. **Requires an EF Core migration** across SQLite/Postgres/SQL Server (mirror the existing migration pattern in `src/Adam.Shared/Migrations/`).
2. **Extractor** (`MetadataExtractorService.cs`) — extend `MapIptc`/`MapXmp`/`MapExifSubIfd` to populate the new fields; add IPTC Extension + XMP Rights + PLUS namespace reads. Add a `GetAllProperties()` helper for the raw viewer (Panel H).
3. **Writeback** (`MetadataWritebackService.cs`) — extend both `BuildXmpPacket` overloads to emit the new fields with correct namespaces, including the **structured** `Iptc4xmpCore:CreatorContactInfo` and `Iptc4xmpExt:LocationCreated/Shown` (currently location is flattened to one string — upgrade to proper structures while keeping back-compat). Reconcile EXIF/IPTC-IIM/XMP per **MWG** guidance (§5).
4. **ViewModel** (`MetadataEditorViewModel.cs`) — add backing properties + bindings for all new editable fields; **make Creator/Copyright/Headline editable**; extend `SaveAsync` to (a) persist all profile fields to the DB and (b) **invoke `MetadataWritebackService`** so edits reach the file's embedded XMP / RAW sidecar (respecting the existing `ReadOnlyFileException` + read-only-file UX).
5. **Round-trip test** — extend `Adam.Shared.Tests` to assert write→read fidelity for each new field and Lightroom/Bridge interop (MWG reconciliation).

### 25.6 Suggested Phasing

| Step | Scope | Effort | Value |
|------|-------|--------|-------|
| **25-A** | Surface already-stored fields (`UsageTerms`, `ContactInfo`, `City/State/Country`, `Orientation`, `GpsAltitude`); make `Creator`/`Copyright`/`Headline` editable; fix `SaveAsync` to persist profile fields + call writeback | 🟢🟡 | ⭐⭐⭐ |
| **25-B** | Regroup editor into collapsible schema panels; add Raw Metadata Viewer (Panel H) | 🟡 | ⭐⭐ |
| **25-C** | Extend model + extractor + writeback to full **IPTC Core** (caption writer, credit, source, instructions, sublocation, country code, date created, genre/scene/subject codes) + migration | 🟡 | ⭐⭐⭐ |
| **25-D** | **IPTC Extension + XMP Rights + PLUS**: releases, licensor, copyright owner, web statement, **Digital Source Type** (AI disclosure); structured contact-info & location-created/shown | 🟡🔴 | ⭐⭐ |
| **25-E** | Dublin Core export mapping + MWG reconciliation round-trip tests | 🟢🟡 | ⭐ |

> **Start with 25-A** — it's the highest value-to-effort: it closes the "stored but invisible / read-only-when-it-shouldn't-be / not-written-back" gaps using fields that already exist, with no schema migration required.

### 25.7 Files Touched
- `src/Adam.Shared/Models/MetadataProfile.cs` (+ new owned types if structured)
- `src/Adam.Shared/Services/MetadataExtractorService.cs`
- `src/Adam.Shared/Services/MetadataWritebackService.cs`
- `src/Adam.CatalogBrowser/ViewModels/MetadataEditorViewModel.cs`
- `src/Adam.CatalogBrowser/Views/MetadataEditorView.axaml`
- `src/Adam.Shared/Migrations/` (new migration; steps 25-C/25-D)
- `tests/Adam.Shared.Tests/` (round-trip fidelity tests)

### 25.8 Structured Field Definitions

Several IPTC Extension / PLUS fields are **structures**, not scalars — they must be modelled as owned types (or related rows) and serialized as nested RDF, not flattened strings. Note: `MetadataExtractor` is **read-only**; the write side is hand-rolled in `BuildXmpPacket`, so each structure below needs explicit emit code.

**Creator Contact Info** — `Iptc4xmpCore:CreatorContactInfo` (single struct):

```csharp
public class CreatorContactInfo   // owned type on MetadataProfile
{
    public string? Address { get; set; }   // CiAdrExtadr
    public string? City    { get; set; }   // CiAdrCity
    public string? Region  { get; set; }   // CiAdrRegion
    public string? PostalCode { get; set; }// CiAdrPcode
    public string? Country { get; set; }   // CiAdrCtry
    public string? Phone   { get; set; }   // CiTelWork
    public string? Email   { get; set; }   // CiEmailWork
    public string? Website { get; set; }   // CiUrlWork
}
```

XMP shape to emit:

```xml
<Iptc4xmpCore:CreatorContactInfo rdf:parseType="Resource">
  <Iptc4xmpCore:CiEmailWork>dean@example.com</Iptc4xmpCore:CiEmailWork>
  <Iptc4xmpCore:CiUrlWork>https://example.com</Iptc4xmpCore:CiUrlWork>
  <Iptc4xmpCore:CiAdrCity>Amalfi</Iptc4xmpCore:CiAdrCity>
  <Iptc4xmpCore:CiAdrCtry>Italy</Iptc4xmpCore:CiAdrCtry>
</Iptc4xmpCore:CreatorContactInfo>
```

**Location (Created / Shown)** — `Iptc4xmpExt:LocationCreated` (single) and `Iptc4xmpExt:LocationShown` (bag of structs). Fields: `Sublocation`, `City`, `ProvinceState`, `CountryName`, `CountryCode`, `WorldRegion`, `LocationId`. Replaces today's flattened `Iptc4xmpCore:Location` string (keep emitting the flat form too for back-compat with older readers).

**Licensor** — `plus:Licensor` (ordered list of structs): `LicensorName`, `LicensorID`, `LicensorURL`, `LicensorEmail`, `LicensorTelephone`. **Copyright Owner** — `plus:CopyrightOwner` (list): `CopyrightOwnerName`, `CopyrightOwnerID`. **Image Creator** — `plus:ImageCreator` (list): `ImageCreatorName`, `ImageCreatorID`.

**People / Org / Event (IPTC Extension, optional but valuable for §4 face/people work):** `Iptc4xmpExt:PersonInImage` (bag of names), `Iptc4xmpExt:OrganisationInImageName` (bag), `Iptc4xmpExt:Event` (lang-alt). These are the natural persistence target for the face/people-tagging feature in §7.

**Artwork or Object** — `Iptc4xmpExt:ArtworkOrObject` (bag of structs: title, creator, date created, source, copyright notice) — relevant for museum/archive/repro workflows.

### 25.9 Controlled Vocabularies (dropdown values)

Editable fields with constrained values must use **dropdowns**, not free text, and store the canonical code (display the friendly label). Source vocabularies:

**Urgency** (`photoshop:Urgency`) — integer `1`–`8` (`1` = most urgent … `8` = least; `0`/unset = none).

**Copyright Status** (`xmpRights:Marked`) — `True` (copyrighted) · `False` (public domain) · *unset* (unknown). Render as a tri-state toggle.

**Model Release Status** (`plus:ModelReleaseStatus`):
| Code | Label |
|------|-------|
| `MR-NON` | None |
| `MR-NAP` | Not Applicable |
| `MR-UMR` | Unlimited Model Releases |
| `MR-LMR` | Limited or Incomplete Model Releases |

**Property Release Status** (`plus:PropertyReleaseStatus`): `PR-NON` None · `PR-NAP` Not Applicable · `PR-UPR` Unlimited Property Releases · `PR-LPR` Limited or Incomplete Property Releases.

**Digital Source Type** (`Iptc4xmpExt:DigitalSourceType`) — the **AI-disclosure** field; store the full IPTC CV URI `http://cv.iptc.org/newscodes/digitalsourcetype/<term>`:
| Term | Label |
|------|-------|
| `digitalCapture` | Original digital capture |
| `negativeFilm` / `positiveFilm` / `print` | Digitised from film/print |
| `minorHumanEdits` | Original capture with minor edits |
| `compositeCapture` | Composite of captures |
| `algorithmicallyEnhanced` | Algorithmically enhanced |
| `trainedAlgorithmicMedia` | **AI-generated** (trained model) |
| `compositeWithTrainedAlgorithmicMedia` | Composite incl. AI-generated |
| `algorithmicMedia` | Pure algorithmic (non-AI-trained) |

**Intellectual Genre** (`Iptc4xmpCore:IntellectualGenre`) — free text, but offer IPTC Genre NewsCodes suggestions (Actuality, Feature, Profile, Daybook, Summary, Wrap-up…).

**EXIF enumerated values (read-only display)** — decode numeric codes to labels: `Orientation` (1–8), `ExposureProgram` (0–8), `MeteringMode` (0–6, 255), `WhiteBalance` (0 Auto / 1 Manual), `ColorSpace` (1 sRGB / 65535 Uncalibrated), `Flash` (bit-mask). `MetadataExtractor` already provides `GetDescription(tag)` for most of these.

### 25.10 Read ⇄ Write Mapping & MWG Reconciliation

Several logical fields live in **multiple blocks** (EXIF, IPTC-IIM, XMP). The Metadata Working Group rules define precedence and synchronization — follow them so Adam interops cleanly with Lightroom/Bridge/Photo Mechanic:

- **Read precedence (overlapping fields):** XMP → IPTC-IIM → EXIF. (Already partially true; make it explicit in the extractor.)
- **Title:** `dc:title` ⇄ IPTC `ObjectName` ⇄ (EXIF none). **Description:** `dc:description` ⇄ IPTC `Caption-Abstract` ⇄ EXIF `ImageDescription`. **Creator:** `dc:creator` ⇄ IPTC `By-line` ⇄ EXIF `Artist`. **Copyright:** `dc:rights` ⇄ IPTC `CopyrightNotice` ⇄ EXIF `Copyright`. On write, emit the XMP form (authoritative) and optionally mirror to IIM for legacy readers.
- **Keywords:** `dc:subject` (flat) ⇄ IPTC `Keywords` ⇄ `lr:hierarchicalSubject` (hierarchy). Keep flat and hierarchical in sync on write (the extractor already prefers hierarchical on read).
- **Dates:** `photoshop:DateCreated` (authoring) is distinct from `exif:DateTimeOriginal` (capture). Surface both; only `DateCreated` is editable.
- **Location:** prefer structured `Iptc4xmpExt:LocationCreated/Shown` on read; fall back to flat `photoshop:City/State/Country` + `Iptc4xmpCore:Location`. Write both structured and flat for compatibility.
- **Implementation note:** because `MetadataExtractor` cannot write, every reconciliation rule above is enforced in `BuildXmpPacket` (write) and the `Map*` methods (read) — keep them as a matched pair and cover each with a round-trip test (step 25-E).

---

## 26. Dedicated Settings Tab (Absorbing Audit)

### 26.1 Context & Problem

Today the top-nav is a row of title-bar buttons — **Gallery · Ingest · Metadata · Audit · (Trash)** — wired in `MainWindow.axaml` via per-VM `DataTemplate`s and `Show*Command`s on `MainWindowViewModel`. There is **no settings surface in the app at all**: the only persisted settings are a handful of connection fields in `AdamConfig` (`~/.adam/CatalogBrowser/settings.json`), with no UI to edit them. "Audit" occupies a whole top-level nav slot for what is really one read-only report. Meanwhile DB-provider/connection config lives only in `Adam.ServiceManager` + the registry.

### 26.2 Proposal

Replace the **Audit** nav entry with **Settings**, and make **Audit a section inside Settings**. Settings becomes a master-detail workspace (left category rail, right detail pane, filter/search box at top), consistent with how OS/IDE settings work.

```
┌─ Settings ──────────────────────────────────────────────────────┐
│ [ 🔍 Search settings… ]                                          │
│ ┌───────────────────┬──────────────────────────────────────────┐│
│ │ General           │  Appearance                               ││
│ │ Appearance      ◀ │   Theme        ( System ▼ )               ││
│ │ Catalog & Storage │   Accent       [■ #1976D2]                ││
│ │ Database & Conn.  │   Density      ( Comfortable ▼ )          ││
│ │ Ingestion         │   Font scale   [──●──] 100%               ││
│ │ Metadata & Schemas│   Language     ( English (US) ▼ )         ││
│ │ AI / LiquidVision │                                           ││
│ │ Search            │  Defaults                                 ││
│ │ Keyboard Shortcuts│   Default view ( Grid ▼ )                 ││
│ │ Security & Session│   Thumb size   [──●────] 160px            ││
│ │ Audit & Activity  │                                           ││
│ │ About & Updates   │                                           ││
│ └───────────────────┴──────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────────────┘
```

### 26.3 Settings Sections (information architecture)

| Section | Contents | Notes |
|---------|----------|-------|
| **General / Appearance** | Theme (light/dark/system/high-contrast), accent color, UI density, font scale, locale, default view (grid/list), default thumbnail size, date/number formats | Ties to §20 a11y + §27 prefs |
| **Catalog & Storage** | Catalog DB location, thumbnail/preview cache location + size cap, backup schedule + "Back up now", integrity check, "Optimize/Vacuum DB" | Ties to §15.3, §16 |
| **Database & Connection** | Mode (Standalone/Multi-user) toggle, broker host/port/TLS, allow self-signed, recent hosts, **Test connection**; (read-only display of broker's DB provider in multi-user) | Replaces hidden `AdamConfig` editing; broker *provider* still owned by ServiceManager |
| **Ingestion** | Watched folders CRUD, default import metadata preset, filename rename template, duplicate-handling policy, backup-on-import, "AI auto-tag on ingest" toggle | Ties to §2 |
| **Metadata & Schemas** | **UI groups**: which §25 schema panels are shown and their order; default metadata presets; controlled-vocabulary management; writeback policy (embed vs sidecar); GPS-strip-on-export default | Ties to §25, §28 |
| **AI / LiquidVision** | Model selection + download manager, execution provider (CPU/GPU via `ExecutionProviderKind`), precision (FP16/FP32), auto-tag triggers, confidence threshold | Ties to §7 |
| **Search** | FTS options, default sort/order, saved-searches management | Ties to §6 |
| **Keyboard Shortcuts** | View + remap the existing `KeyBinding`s; reset to defaults | The bindings already exist in `MainWindow.axaml` |
| **Security & Session** *(multi-user)* | Session timeout, remember username, TLS cert trust, sign-out | Role-gated |
| **Audit & Activity** | The relocated **Audit log** (`AuditLogViewModel`) with its filters, plus access logs and a "this device" activity view | Gated by existing `CanAudit` |
| **About & Updates** | Version, check-for-updates (§19.1), release notes, **in-app log viewer** (§19.3), "export diagnostics bundle" | — |

### 26.4 Implementation Sketch

- New `SettingsViewModel` (host) + `SettingsView.axaml` (master-detail). Each section is a child VM/`UserControl`; render the active one via the existing `DataTemplate` pattern.
- Replace the Audit nav button with **Settings**; move `AuditLogView` under the *Audit & Activity* section (keep `AuditLogViewModel` as-is, just re-host it).
- **Permission gating** reuses existing flags: Audit section behind `CanAudit`; admin-only sections (user/role management) link out to `ServiceManager` or are hidden by role.
- Settings read/write through the new preference layer in **§27** (catalog-stored) for portable prefs, and keep machine-local values (paths, endpoints, window geometry) in `AdamConfig`. See §27.3 for the split.
- Effort 🔴 (new workspace) but high structural value ⭐⭐⭐ — it's the home that many other features (themes, presets, AI config, saved searches, updates) need to land in.

---

## 27. Persisting User Preferences & UI State to the Catalog

### 27.1 Context & Problem

`AdamConfig` persists only connection fields to a local JSON file. **No UI state is persisted** — every launch resets view mode, thumbnail size, sort/filter, sidebar width, expanded nodes, window geometry, and (once §25/§28 land) metadata-panel layout. The request: persist user preferences **and** UI state as a JSON object **into the catalog**, so it travels with the catalog and (in multi-user) follows the user across devices.

### 27.2 Two-Tier Persistence Model (key design decision)

Not everything belongs in the catalog. Split by portability:

| Tier | Where | Examples |
|------|-------|----------|
| **Machine-local** | stays in `AdamConfig` JSON (per-device) | catalog/cache paths, broker endpoints + TLS, window geometry per-monitor, "last opened catalog" |
| **Portable preferences + UI state** | **new: stored in the catalog DB as JSON** | theme/accent/density/locale, default view + thumbnail size, gallery sort/columns/widths, sidebar width + expanded nodes, metadata-panel order/expansion, saved-search defaults, recent searches, ingest defaults |

This keeps the catalog portable (no machine-specific paths leak in) while making the *experience* follow the user.

### 27.3 Data Model

A small key→JSON table in `AppDbContext`, scoped by user (null/sentinel user = standalone single-user):

```csharp
public class UserPreference
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }     // null = standalone/local user
    public string Key { get; set; } = "ui"; // e.g. "ui.state", "ui.prefs"
    public string ValueJson { get; set; } = "{}"; // jsonb / nvarchar(max) / TEXT
    public DateTime UpdatedAt { get; set; }
    public uint Version { get; set; }     // concurrency token (matches existing pattern)
}
```

- **Provider-native JSON:** Postgres `jsonb`, SQL Server `nvarchar(max)` (JSON), SQLite `TEXT`. EF Core can map an owned type via `ToJson()`, or store a serialized string and (de)serialize in a service — the string approach is simplest and provider-portable. **Requires a migration** across all three providers (mirror `src/Adam.Shared/Migrations/`).
- **Unique index** on `(UserId, Key)`.

### 27.4 The Preferences JSON (shape + versioning)

```jsonc
{
  "schemaVersion": 1,
  "appearance": { "theme": "system", "accent": "#1976D2", "density": "comfortable", "fontScale": 1.0, "locale": "en-US" },
  "gallery":    { "viewMode": "grid", "thumbnailSize": 160, "sortBy": "dateTaken", "sortDir": "desc",
                  "pageSize": 50, "visibleColumns": ["name","rating","dateTaken","camera"], "columnWidths": { "name": 240 } },
  "sidebar":    { "width": 280, "collapsed": false, "expandedNodes": ["collections","keywords"] },
  "metadata":   { "panelOrder": ["description","creator","rights","location","camera","dates","gps","raw"],
                  "expandedPanels": ["description","creator","rights"], "showSchemaTooltips": true },
  "ingest":     { "autoAiTag": false, "defaultPresetId": null },
  "recentSearches": [],
  "lastView": "gallery"
}
```

- **`schemaVersion`** drives forward-migration of the blob; unknown keys are preserved (round-trip tolerant) so older/newer clients don't clobber each other's settings.
- Window geometry intentionally **excluded** (machine-local → `AdamConfig`).

### 27.5 Service & Sync Design

- **`IUserPreferenceService`** in `Adam.Shared`: `Task<T> GetAsync<T>(key)`, `Task SetAsync(key, obj)`, typed accessors, `ResetAsync()`. Serializes with `System.Text.Json`.
- **Standalone:** read/write the `UserPreference` row directly via `ModeManager.CreateDbContextAsync`.
- **Multi-user:** the client talks to the broker, not the DB — add a **`PreferenceHandler`** + protobuf contract so prefs load/save through `BrokerClient`, scoped to the authenticated `UserId`. This also lets a user's workspace follow them to any client machine.
- **Apply on startup** (after mode/auth resolved) → push into the relevant VMs; **debounced autosave** (~1–2 s after last change) + save on exit.
- **Concurrency:** last-write-wins via `Version` (same user, two devices), consistent with the app's existing concurrency model; or device-scoped sub-keys if per-device layouts are wanted later.
- **"Reset to defaults"** action in Settings (§26) clears the blob.
- Effort 🟡–🔴, value ⭐⭐⭐ — foundational for §26 and a visible quality-of-life win.

---

## 28. Metadata Workspace Rework

§25 expands the *fields*; this section reworks the *experience*. Today's Metadata tab is a single static form: a filename header, four editable fields (title/description/tags/rating), and a read-only camera-data column — no image, no batch editing, no presets, no AI assist, no navigation. Proposed redesign into a **Metadata Workspace**:

### 28.1 Layout

```
┌─ Metadata Workspace ─────────────────────────────────────────────────────────┐
│ ┌──────────────┐ ┌──────────────────────────────┐ ┌───────────────────────┐ │
│ │              │ │ ▼ Description & Content   (E) │ │ Completeness   78% ▓▓░ │ │
│ │   preview    │ │   Title    [ … ]             │ │ Missing: Rights, City  │ │
│ │   (loupe/    │ │   Keywords [a][b](+)         │ │                        │ │
│ │   thumbnail) │ │ ▼ Creator & Contact      (E) │ │ ✦ AI Suggestions       │ │
│ │              │ │ ▼ Rights & Licensing     (E) │ │  “harbour at dusk” [✓][✗]│ │
│ │              │ │ ▼ Location  [ map pin ]  (E) │ │  +keywords: pier,boat  │ │
│ ├──────────────┤ │ ▶ Dates / Camera / GPS   (R) │ │                        │ │
│ │ ◀ filmstrip ▶│ │ ▶ All Metadata (raw)     (R) │ │ Source: ⓘ per-field    │ │
│ │ ▢▢▣▢▢▢▢▢      │ │                              │ │ Writeback: ✎ sidecar   │ │
│ └──────────────┘ └──────────────────────────────┘ └───────────────────────┘ │
│  3 assets selected · [Apply to all] [Save] [Preset ▼]   ● unsaved changes     │
└───────────────────────────────────────────────────────────────────────────────┘
```

Three panes: **left** preview + filmstrip (navigate/tag a series without leaving the tab), **center** the collapsible §25 schema panels, **right** an assist rail (completeness, AI suggestions, provenance, writeback status).

### 28.2 Best-Idea Feature Set

| Idea | What | Effort | Value |
|------|------|--------|-------|
| **Image-aware editing** | Show a preview (thumbnail→loupe) beside the form — you see what you're tagging (today there is none) | 🟡 | ⭐⭐⭐ |
| **Batch / multi-asset edit** | On multi-select, show shared values, "⟨mixed⟩" indicators, edit-once-apply-to-all, "sync metadata from active asset" | 🟡 | ⭐⭐⭐ |
| **Metadata presets/templates** | One-click apply saved sets (copyright/creator/rights/location); "create preset from this asset"; managed in Settings §26 | 🟢🟡 | ⭐⭐⭐ |
| **Configurable panels (UI groups)** | Reorder/show/hide the §25 schema panels; persisted via §27; controlled in Settings §26 | 🟡 | ⭐⭐ |
| **Completeness / quality meter** | % of recommended fields filled; highlight missing rights/creator/location; per-collection "required fields" policy | 🟡 | ⭐⭐ |
| **Inline AI assist** | AI description/keywords/alt-text shown as accept/reject chips with confidence (pending suggestions, not auto-applied) — extends the current `AutoTagCommand` | 🟡 | ⭐⭐⭐ |
| **Keyword tooling** | Hierarchical keyword tree w/ checkboxes, keyword sets/palettes, recently-used, drag from sidebar; synonyms (§4) | 🟡 | ⭐⭐ |
| **Map widget for GPS** | Drag-pin geotagging + reverse-geocode to place names; ties to §4 map module | 🟡 | ⭐⭐ |
| **Per-field provenance** | Small indicator of a value's origin (EXIF / IPTC / XMP / AI / manual) | 🟢 | ⭐ |
| **External-change reconciliation** | Banner when the file's on-disk XMP differs from the catalog (edited in Lightroom/Bridge); choose authority | 🟡 | ⭐⭐ |
| **Field history / revert** | Show what changed, by whom (multi-user `AccessLog`), revert a single field | 🟡 | ⭐⭐ |
| **Writeback status & control** | Indicator: embed vs sidecar, read-only-file warning (reuse `ReadOnlyFileException`), last-written time, "Write to file now" vs auto | 🟢 | ⭐⭐ |
| **Filmstrip navigation** | Prev/next within the current selection/gallery filter so you can tag a shoot rapidly | 🟢 | ⭐⭐ |
| **Copy/paste metadata** | Copy a field or whole metadata set between assets | 🟢 | ⭐ |
| **Save UX** | Dirty indicator, optional autosave, undo; extends the existing dirty/`SaveCommand` flow | 🟢 | ⭐⭐ |

### 28.3 Relationship to Other Sections
- **Field-level detail** (which fields, schemas, structures, vocabularies) lives in **§25** — this section is the surrounding workspace.
- **AI suggestion chips** build on §7 (confidence/review queue) and the existing `AiTaggingService` + `AutoTagCommand`.
- **Preview/loupe** shares the §3 loupe component once built.
- **Panel layout + presets persist** via §27; managed in §26.
- **Files:** `MetadataEditorView.axaml` + `MetadataEditorViewModel.cs` (rework), new preset/assist child VMs, `MetadataWritebackService.cs` (status surfacing), and the §27 preference store for layout.

---

*Generated 2026-06-13. Grounded in the current codebase (`src/`), planning docs (`.planning/`), stated v1 limitations, and the established DAM feature landscape (Lightroom Classic, Capture One, Photo Mechanic, digiKam, Bridge; Bynder, Brandfolder, Canto, MediaValet, AEM Assets, Cloudinary) plus relevant standards (IPTC, XMP, MWG, Dublin Core, PLUS, C2PA). Effort/value ratings are first-pass estimates for prioritization, not commitments. Live web verification was attempted but blocked by an account usage limit; claims about competitor features reflect general domain knowledge and should be spot-checked before committing roadmap resources.*
