---
goal: Facial Recognition Pipeline — YuNet face detection + ArcFace face recognition ONNX pipeline, per-person HDBSCAN clustering, full person management UI
version: 1.0
date_created: 2026-06-18
date_updated: 2026-06-18
status: Planned — execution ready
phase: 23
milestone: v5.0 — AI-Native DAM
tags: [phase-23, facial-recognition, onnx, person-management, ai-pipeline]
---

# Phase 23 — Facial Recognition Pipeline

**v5.0 — AI-Native DAM (Part 2 of ?)**

## Context

Phase 23 delivers a **two-stage ONNX pipeline** for facial recognition that runs during AI tagging. Face detection uses **YuNet** (OpenCV Zoo, ~337 KB ONNX from HuggingFace), and face recognition uses **ArcFace** (`garavv/arcface-onnx`, ~8 MB ONNX from HuggingFace). Combined download: **~8.3 MB** — negligible compared to the LFM2-VL models (250 MB–3.2 GB).

| Feature | Existing Infrastructure | Gap |
|---------|------------------------|-----|
| **ONNX model download** | `ModelDownloader` (retry+resume), `ModelCacheManager`, `ModelVerificationMarker` | Layout is LFM2-VL-specific; need `FaceModelLayout` or generalize |
| **ONNX inference** | `InferenceSession` via `Microsoft.ML.OnnxRuntime` | Need new analyzer interface and pipeline for face models |
| **Image processing** | `ImagePreprocessor` (SkiaSharp-based decode, resize, colorspace) | Need face-specific: affine alignment from 5 landmarks, 112×112 crop |
| **Embedding storage** | `AssetEmbedding` (384-dim text + 4096-dim image vectors) | Need new `AssetFace` table (512-dim face vectors, bounding box, person FK) |
| **AI Tagging pipeline** | `AiTaggingService.TagAssetsAsync` (sequential per-asset tagging) | Need post-tagging face detection hook |
| **Multi-user architecture** | Broker handlers, message contracts, DI singletons | Need `FaceHandler` + `PersonHandler` |

## Architecture Overview

```
                            ┌──────────────────────┐
Asset Image ──────────────→ │    FaceDetection      │
(decoded via SkiaSharp)     │    (YuNet ONNX)       │
                            └──────────┬───────────┘
                                       │ Bounding Box + 5 Landmarks
                                       ▼
                            ┌──────────────────────┐
                            │    Face Alignment     │
                            │  (affine transform)   │
                            └──────────┬───────────┘
                                       │ Aligned 112×112 crop
                                       ▼
                            ┌──────────────────────┐
                            │   Face Recognition    │
                            │   (ArcFace ONNX)      │
                            └──────────┬───────────┘
                                       │ 512-dim embedding
                                       ▼
                ┌──────────────────────────────────────────┐
                │           FaceMatcherService              │
                │                                          │
                │  Cosine Similarity vs Known Person Centroids │
                │                                          │
                │  >0.85 → Auto-assign priority             │
                │  0.70–0.85 → Suggest (flag for review)    │
                │  <0.70 → Store as Unknown for clustering   │
                └──────────────────────────────────────────┘
```

### Model Specification

**ArcFace** (`garavv/arcface-onnx` on HuggingFace):
- Model file: `arc.onnx` (~8 MB)
- Download: `https://huggingface.co/garavv/arcface-onnx/resolve/main/arc.onnx?download=true`
- Input shape: `(1, 112, 112, 3)` — NHWC, RGB, normalized `(pixels - 127.5) / 128.0`
- Output: 512-dim float32 embedding vector
- Input tensor name: dynamic (`session.InputMetadata.Keys.First()`)
- Alternative (larger): `onnxmodelzoo/arcfaceresnet100-8` (ResNet100, ~250 MB, same 512-dim output)

**YuNet** (`opencv/face_detection_yunet` on HuggingFace + OpenCV Zoo on GitHub):
- Model file: `face_detection_yunet_2026may.onnx` (~337 KB)
- GitHub download: `https://github.com/opencv/opencv_zoo/raw/main/models/face_detection_yunet/face_detection_yunet_2026may.onnx`
- HuggingFace download: `https://huggingface.co/opencv/face_detection_yunet/resolve/main/face_detection_yunet_2026may.onnx`
- Input shape: `(1, 3, H, W)` — NCHW, BGR (dynamic H/W, typically 320×320)
- Output: Raw detection tensors requiring anchor decoding + NMS post-processing (~250 LOC C#)
- Legacy fallback: `face_detection_yunet_2023mar.onnx` (fixed 320×320 input)

## Locked Decisions (from Discussion)

| # | Decision | Choice |
|---|----------|--------|
| A | Pipeline trigger | During AI tagging (post-tag hook in `AiTaggingService`) |
| B | Face scope | Only AI-tagged assets (images with existing `AssetEmbedding`) |
| C | Matching algorithm | HDBSCAN clustering → nearest-neighbor centroid matching after naming |
| D | Confidence thresholds | Auto-assign >0.85, Suggest 0.70–0.85, Unknown <0.70 |
| E | Model source | HuggingFace hub (ArcFace: `garavv/arcface-onnx`, YuNet: `opencv/face_detection_yunet`) |
| F | Person management | Full: list, merge, rename, delete, view person gallery |
| G | Multi-user support | Broker messages: `DetectFacesRequest/Response`, `NamePersonRequest/Response` |
| H | Data models | `Person` + `AssetFace` entities with FK relationships |

## Tasks

### T23.1 — Face Model Infrastructure (Shared, LiquidVision.Core)

**Goal:** Create reusable model layout, downloader, and inference infrastructure for face detection and recognition. Extends the existing LiquidVision.Core patterns without coupling to LFM2-VL specifics.

**Files:**
- `src/LiquidVision.Core/Configuration/FaceModelLayout.cs` (new)
- `src/LiquidVision.Core/Services/FaceDetectionService.cs` (new)
- `src/LiquidVision.Core/Services/FaceRecognitionService.cs` (new)
- `src/LiquidVision.Core/Interfaces/IFaceService.cs` (new)

**FaceModelLayout:** A generalized model layout for face models, decoupled from the LFM2-VL-specific layout. Supports one-file-per-model (unlike LFM2-VL's multi-file layout with ONNX graphs + data shards + configs).

```csharp
public sealed class FaceModelLayout
{
    public string BaseUrl { get; }
    public string ModelDirectory { get; }
    public string LocalOnnxPath { get; }
    public string ModelVersion { get; }

    public IReadOnlyList<RemoteModelFile> RemoteFiles { get; }

    public static FaceModelLayout YuNet(LiquidVisionOptions options, string cacheRoot);
    public static FaceModelLayout ArcFace(LiquidVisionOptions options, string cacheRoot);
}
```

- YuNet layout: single file `face_detection_yunet_2026may.onnx` (~337 KB)
- ArcFace layout: single file `arc.onnx` (~8 MB)
- Both use the existing `ModelDownloader` with HuggingFace/GitHub URLs
- Cache under `%LOCALAPPDATA%/Adam/models/face/yuet/` and `.../arcface/`

**FaceDetectionService:**
```csharp
public sealed class FaceDetectionService : IAsyncDisposable
{
    public FaceDetectionService(FaceModelLayout layout, LiquidVisionOptions options);

    public async Task InitializeAsync(CancellationToken ct);
    public async Task<IReadOnlyList<DetectedFace>> DetectFacesAsync(byte[] imageData, CancellationToken ct);

    public bool IsInitialized { get; }
    public double DownloadProgress { get; }
}

public sealed record DetectedFace
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
    public float Confidence { get; init; }
    public IReadOnlyList<(float X, float Y)>? Landmarks { get; init; } // 5 points: eyes, nose, mouth corners
}
```

- Downloads model on first call via `ModelDownloader`
- Creates ONNX `InferenceSession` on initialization
- Wraps the output decoding: anchor decoding (from raw tensors → bounding boxes + landmarks), confidence filter (>0.5 default), NMS (IoU threshold 0.3)
- Uses SkiaSharp to decode and resize the input image (reuses the pattern from `ImagePreprocessor` but simpler — just resize to 320×320)

**FaceRecognitionService:**
```csharp
public sealed class FaceRecognitionService : IAsyncDisposable
{
    public FaceRecognitionService(FaceModelLayout layout, LiquidVisionOptions options);

    public async Task InitializeAsync(CancellationToken ct);
    public async Task<float[]> GetFaceEmbeddingAsync(byte[] alignedFaceCrop, CancellationToken ct);

    public bool IsInitialized { get; }
    public double DownloadProgress { get; }
}
```

- Downloads `arc.onnx` on first call via `ModelDownloader`
- Creates ONNX `InferenceSession` on initialization
- Preprocessing: normalize `(pixels - 127.5) / 128.0`, convert to NHWC tensor (1, 112, 112, 3)
- Returns 512-dim float32 embedding
- Input tensor name detected dynamically

**IFaceService interface:**
```csharp
public interface IFaceService : INotifyPropertyChanged, IAsyncDisposable
{
    Task InitializeAsync(IProgress<double>? progress, CancellationToken ct);
    bool IsInitialized { get; }
    double DownloadProgress { get; }
}
```
Both services implement this for consistent initialization pattern matching `ILiquidVisionAnalyzer`.

**Estimated LOC:** ~250

---

### T23.2 — Face Alignment

**Goal:** Convert the raw output from YuNet (bounding box + 5 landmarks) into an aligned 112×112 face crop suitable for ArcFace input. Uses SkiaSharp (already a dependency of LiquidVision.Core).

**Files:**
- `src/Adam.Shared/Services/FaceAligner.cs` (new - in Shared so it's available to both standalone and broker)

**Algorithm:**
1. Extract the 5 facial landmarks from YuNet output: left eye, right eye, nose, left mouth corner, right mouth corner
2. Compute the affine transformation matrix that aligns the eyes to a canonical position (eyes horizontal, centered at rows 40 and 40, 48 pixels apart)
3. Apply `SKBitmap.ApplyTransform(affineMatrix)` with Mitchell cubic resampling
4. Crop the result to 112×112

```csharp
public sealed class FaceAligner
{
    /// <summary>
    /// Aligns a face crop from the full image using YuNet's 5 landmarks.
    /// </summary>
    /// <param name="fullImage">The original decoded image bytes.</param>
    /// <param name="face">The detected face with bounding box and landmarks.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aligned 112×112 RGB pixel data as byte[112*112*3] in NHWC layout.</returns>
    public byte[] AlignFace(byte[] fullImageData, DetectedFace face);

    /// <summary>
    /// Crops the aligned face region as a thumbnail (e.g. 64×64 for display).
    /// </summary>
    public byte[] ExtractThumbnail(byte[] alignedFace, int size = 64);
}
```

**Reference landmarks (canonical positions):**
```csharp
// Normalized canonical landmark positions for alignment target
private static readonly (float X, float Y)[] CanonicalLandmarks = new[]
{
    (38.2946f, 51.6963f),  // left eye
    (73.5318f, 51.5014f),  // right eye
    (56.0252f, 71.7366f),  // nose tip
    (41.5493f, 92.3655f),  // left mouth corner
    (70.7299f, 92.2041f),  // right mouth corner
};
```

**Estimated LOC:** ~100

---

### T23.3 — Data Models & Database (AppDbContext)

**Goal:** Create the `Person` and `AssetFace` entities, add them to `AppDbContext`, and create the EF Core migration.

**Files:**
- `src/Adam.Shared/Models/Person.cs` (new)
- `src/Adam.Shared/Models/AssetFace.cs` (new)
- `src/Adam.Shared/Data/AppDbContext.cs` (add DbSets + config + index)
- `src/Adam.Shared/Migrations/20260618_AddFacialRecognition.cs` (new migration)

**Person entity:**
```csharp
public sealed class Person
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public byte[]? ThumbnailImage { get; set; }  // representative face crop (64×64 JPEG)
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }

    // Computed field: averaged 512-dim centroid embedding (serialized as byte[2048])
    public byte[]? CentroidEmbedding { get; set; }

    // Model version that produced this centroid
    public string? EmbeddingModelVersion { get; set; }

    public ICollection<AssetFace> Faces { get; set; } = [];
}
```

**AssetFace entity:**
```csharp
public sealed class AssetFace
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public Guid? PersonId { get; set; }

    /// <summary>512-dim float32 embedding serialized as byte[2048] (512 × 4 bytes).</summary>
    public byte[] FaceEmbedding { get; set; } = [];

    /// <summary>Bounding box and landmarks as JSON: {"x":100,"y":50,"w":80,"h":80,"landmarks":[{...}]}</summary>
    public string BoundingBoxJson { get; set; } = "{}";

    /// <summary>Detection confidence from YuNet (0.0–1.0).</summary>
    public float DetectionConfidence { get; set; }

    /// <summary>Matching confidence against known person centroid (0.0–1.0).</summary>
    public float MatchingConfidence { get; set; }

    /// <summary>True when auto-assigned (>0.85), false when suggested or unknown.</summary>
    public bool IsAutoAssigned { get; set; }

    /// <summary>64×64 JPEG thumbnail of the aligned face for UI display.</summary>
    public byte[]? ThumbnailImage { get; set; }

    public DigitalAsset Asset { get; set; } = null!;
    public Person? Person { get; set; }
}
```

**AppDbContext additions:**
```csharp
public DbSet<Person> Persons => Set<Person>();
public DbSet<AssetFace> AssetFaces => Set<AssetFace>();

// In OnModelCreating:

modelBuilder.Entity<Person>(e =>
{
    e.HasKey(x => x.Id);
    e.Property(x => x.Name).IsRequired().HasMaxLength(200);
    e.Property(x => x.Notes).HasMaxLength(4000);
    e.Property(x => x.ThumbnailImage);
    e.Property(x => x.CentroidEmbedding);  // byte[] for raw float32
    e.Property(x => x.EmbeddingModelVersion).HasMaxLength(100);
    e.HasIndex(x => x.Name).IsUnique();
    e.HasIndex(x => x.CreatedAt);
});

modelBuilder.Entity<AssetFace>(e =>
{
    e.HasKey(x => x.Id);
    e.Property(x => x.FaceEmbedding).IsRequired();
    e.Property(x => x.BoundingBoxJson).IsRequired().HasMaxLength(1000);
    e.Property(x => x.ThumbnailImage);
    e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
    e.HasOne(x => x.Person).WithMany(p => p.Faces).HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.SetNull);
    e.HasIndex(x => x.AssetId);
    e.HasIndex(x => x.PersonId);
    e.HasIndex(x => x.DetectionConfidence);
});
```

**Estimated LOC:** ~110

---

### T23.4 — Face Matching Service (HDBSCAN Clustering)

**Goal:** Implement the core face matching logic: cosine similarity comparison, HDBSCAN-style clustering for unknown faces, centroid computation, and confidence-gated auto-assignment.

**Files:**
- `src/Adam.Shared/Services/FaceMatcherService.cs` (new)

**FaceMatcherService:**
```csharp
public sealed class FaceMatcherService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    // ── Configuration ──
    public double AutoAssignThreshold { get; set; } = 0.85;
    public double SuggestThreshold { get; set; } = 0.70;
    public int MinClusterSize { get; set; } = 3;
    public double ClusterSimilarityThreshold { get; set; } = 0.75;

    /// <summary>
    /// Matches a detected face against known persons and returns the best match.
    /// </summary>
    public async Task<FaceMatchResult> MatchAsync(
        Guid assetFaceId, CancellationToken ct = default);

    /// <summary>
    /// Batch: for all unmatched faces, run matching against known person centroids.
    /// </summary>
    public async Task BatchMatchAsync(
        IProgress<(int completed, int total)>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Clusters unknown faces into candidate person groups using HDBSCAN.
    /// Returns suggested person clusters with centroid embeddings.
    /// </summary>
    public async Task<IReadOnlyList<PersonCluster>> ClusterUnknownFacesAsync(
        IProgress<(int completed, int total)>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Computes the centroid embedding for a person from all their known faces.
    /// </summary>
    public async Task<byte[]> ComputeCentroidAsync(Guid personId, CancellationToken ct = default);

    /// <summary>
    /// Cosine similarity between two face embeddings (reuses existing pattern).
    /// </summary>
    public static float CosineSimilarity(byte[] emb1, byte[] emb2);
}

public sealed record FaceMatchResult
{
    public Guid AssetFaceId { get; init; }
    public Guid? MatchedPersonId { get; init; }
    public string? MatchedPersonName { get; init; }
    public float Confidence { get; init; }
    public FaceMatchType MatchType { get; init; } // AutoAssigned, Suggested, Unknown
}

public sealed record PersonCluster
{
    public string SuggestedName { get; init; }
    public int FaceCount { get; init; }
    public byte[] CentroidEmbedding { get; init; }
    public IReadOnlyList<Guid> AssetFaceIds { get; init; }
    public float AvgConfidence { get; init; }
    public IReadOnlyList<string> CommonAssetKeywords { get; init; } // for naming suggestions
}

public enum FaceMatchType { AutoAssigned, Suggested, Unknown }
```

**Algorithm:**
1. **MatchAsync:** Load the face embedding → compute cosine similarity against all known person centroids → find best match → apply threshold gating
2. **BatchMatchAsync:** Load all unmatched faces → for each, find best centroid match → batch-update DB
3. **ClusterUnknownFacesAsync:** Load all unmatched embeddings → build pairwise similarity matrix (SIMD via existing `CosineSimilarity`) → connected components from mutual similarity neighbors (same simplified HDBSCAN approach as `EmbeddingClusterService` in Phase 22) → generate suggested names from common asset keywords
4. **ComputeCentroidAsync:** Average all face embeddings for a person → re-normalize to unit vector

**Edge cases:**
- No known persons → all faces are "unknown" (ready for clustering)
- Empty DB → return empty results
- Single-face cluster → skip (< `MinClusterSize`)

**Estimated LOC:** ~200

---

### T23.5 — Broker Messages & Handlers (Multi-User)

**Goal:** Define broker message contracts for face/person operations and implement handlers on the broker side.

**Files:**
- `src/Adam.Shared/Contracts/MessageTypeCode.cs` (add 4 new opcodes)
- `src/Adam.Shared/Contracts/FaceMessages.cs` (new)
- `src/Adam.BrokerService/Handlers/FaceHandler.cs` (new)
- `src/Adam.BrokerService/Handlers/PersonHandler.cs` (new)
- `src/Adam.BrokerService/Handlers/ConnectionHandler.cs` (register handlers)
- `src/Adam.BrokerService/Program.cs` (DI registration)

**New opcodes (170-179 range):**
```csharp
DetectFacesRequest = 170,
DetectFacesResponse = 171,
NamePersonRequest = 172,
NamePersonResponse = 173,
ListPersonsRequest = 174,
ListPersonsResponse = 175,
MergePersonsRequest = 176,
MergePersonsResponse = 177,
DeletePersonRequest = 178,
DeletePersonResponse = 179,
```

**FaceMessages.cs contracts:**
```csharp
[ProtoContract]
public sealed record DetectFacesRequest : IProtoSerializable
{
    [ProtoMember(1)] public string AssetId { get; init; }  // UUID string
}

[ProtoContract]
public sealed record DetectFacesResponse : IProtoSerializable
{
    [ProtoMember(1)] public string AssetId { get; init; }
    [ProtoMember(2)] public int FaceCount { get; init; }
    [ProtoMember(3)] public string? ErrorMessage { get; init; }
}

[ProtoContract]
public sealed record NamePersonRequest : IProtoSerializable
{
    [ProtoMember(1)] public string AssetFaceId { get; init; }
    [ProtoMember(2)] public string PersonName { get; init; }
}

[ProtoContract]
public sealed record NamePersonResponse : IProtoSerializable
{
    [ProtoMember(1)] public string PersonId { get; init; }
    [ProtoMember(2)] public string? ErrorMessage { get; init; }
}

// Plus ListPersons, MergePersons, DeletePerson contracts...
```

**FaceHandler:**
```csharp
public sealed class FaceHandler
{
    private readonly FaceDetectionService _detection;
    private readonly FaceRecognitionService _recognition;
    private readonly FaceAligner _aligner;
    private readonly FaceMatcherService _matcher;
    private readonly AppDbContext _db;

    public async Task<DetectFacesResponse> DetectFacesAsync(
        DetectFacesRequest req, CancellationToken ct);
    // 1. Load asset from DB, verify image type
    // 2. Run YuNet face detection
    // 3. For each detected face:
    //    a. Crop + align via FaceAligner
    //    b. Run ArcFace to get 512-dim embedding
    //    c. Match against known persons via FaceMatcherService
    //    d. Create AssetFace record (with auto-assign/suggest/unknown status)
    // 4. Return count of detected faces
}
```

**PersonHandler:**
```csharp
public sealed class PersonHandler
{
    private readonly AppDbContext _db;

    public async Task<ListPersonsResponse> ListPersonsAsync(
        ListPersonsRequest req, CancellationToken ct);
    public async Task<NamePersonResponse> NamePersonAsync(
        NamePersonRequest req, CancellationToken ct);
    public async Task<MergePersonsResponse> MergePersonsAsync(
        MergePersonsRequest req, CancellationToken ct);
    public async Task DeletePersonAsync(
        DeletePersonRequest req, CancellationToken ct);
}
```

**DI registration in ConnectionHandler.cs:** Map `MessageTypeCode.DetectFacesRequest → faceHandler.DetectFacesAsync`, etc.

**Estimated LOC:** ~200

---

### T23.6 — Face Detection during AI Tagging (Pipeline Integration)

**Goal:** Hook face detection into the existing `AiTaggingService.TagAssetsAsync` pipeline so it runs automatically after AI tagging completes on image assets.

**Files:**
- `src/Adam.Shared/Services/FaceDetectionPipelineService.cs` (new)
- `src/Adam.CatalogBrowser/App.axaml.cs` (DI registration)
- `src/Adam.BrokerService/Program.cs` (DI registration)

**FaceDetectionPipelineService:**
```csharp
/// <summary>
/// Orchestrates the face detection pipeline after AI tagging completes.
/// Runs YuNet → FaceAligner → ArcFace → FaceMatcher for each image asset.
/// </summary>
public sealed class FaceDetectionPipelineService
{
    public async Task ProcessAssetsAsync(
        IEnumerable<Guid> assetIds,
        IProgress<(int completed, int total)>? progress = null,
        CancellationToken ct = default);

    public async Task ProcessAssetAsync(
        Guid assetId, CancellationToken ct = default);
}
```

**Integration point in AiTaggingService:**
After `TagAssetAsync` completes successfully (keywords/categories/description written), optionally call `_facePipeline.ProcessAssetAsync(assetId, ct)`. The connection is made via a new `FaceDetectionPipelineService` that is injected into the ingestion flow rather than directly into `AiTaggingService`, to keep the tagging service focused and loosely coupled.

**Wiring approach:**
1. `IngestionViewModel` already orchestrates the tagging pipeline (calls `AiTaggingService.TagAssetsAsync`)
2. After `TagAssetsAsync` completes, add a second sequential pass: `FaceDetectionPipelineService.ProcessAssetsAsync(taggedImageIds, progress)`
3. This runs in the same "AI Tagging" status bar context — users see: "Tagging assets... (5/20)" then "Detecting faces... (5/20)"

**Estimated LOC:** ~70

---

### T23.7 — UI: Face Tagging View

**Goal:** A full-page view showing all detected faces grouped by person, with the ability to name/confirm/suggest faces.

**Components:**
- **FaceTaggingViewModel** — state management for browsing and naming faces
- **FaceTaggingView** — XAML with person cards and face thumbnails

**Files:**
- `src/Adam.CatalogBrowser/ViewModels/FaceTaggingViewModel.cs` (new)
- `src/Adam.CatalogBrowser/Views/FaceTaggingView.axaml` (new)
- `src/Adam.CatalogBrowser/Views/FaceTaggingView.axaml.cs` (new)
- `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` (add nav button + DataTemplate)
- `src/Adam.CatalogBrowser/Views/MainWindow.axaml` (add nav button + DataTemplate)

**FaceTaggingViewModel:**
```csharp
public sealed class FaceTaggingViewModel : INotifyPropertyChanged
{
    // ── State ──
    public IReadOnlyList<PersonGroup> Persons { get; }
    public PersonGroup? SelectedPerson { get; set; }
    public IReadOnlyList<AssetFaceItem> UnknownFaces { get; }
    public bool IsLoading { get; }
    public bool HasUnknownFaces { get; }
    public int TotalFaceCount { get; }

    // ── Commands ──
    public ICommand NameFaceCommand { get; }     // Assign a name to a face / create person
    public ICommand SuggestNamesCommand { get; } // Run HDBSCAN clustering for unknown faces
    public ICommand ConfirmFaceCommand { get; }  // Confirm a suggested match (0.70-0.85 range)
    public ICommand RejectFaceCommand { get; }   // Reject a suggested match
    public ICommand RefreshCommand { get; }      // Reload from DB
    public ICommand OpenPersonGalleryCommand { get; } // Show all assets for this person
}

public sealed record PersonGroup
{
    public Guid PersonId { get; init; }
    public string Name { get; init; }
    public int FaceCount { get; init; }
    public byte[]? Thumbnail { get; init; }
    public IReadOnlyList<AssetFaceItem> Faces { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record AssetFaceItem
{
    public Guid FaceId { get; init; }
    public Guid AssetId { get; init; }
    public byte[]? Thumbnail { get; init; }
    public float MatchingConfidence { get; init; }
    public FaceMatchType MatchType { get; init; }
    public string? AssetFileName { get; init; }
}
```

**FaceTaggingView layout:**
```
┌─ Face Recognition ─────────────────────────────────[✕ Close]─┐
│                                                                 │
│  [Refresh]  [Suggest Names from Unknowns]                       │
│                                                                 │
│  Known Persons (12)                    Unknown Faces (8)        │
│  ┌─────────────────────┐             ┌─────────────────────┐   │
│  │ 👤 Alice Johnson    │  ⬡         │   unnamed_001.jpg   │   │
│  │   23 faces ★★★★☆   │  ⬡         │  [Name...] [Suggest]│   │
│  │                     │  ⬡         │  ┌───┐ ┌───┐        │   │
│  │ 👤 Bob Smith        │  ⬡         │  │   │ │   │        │   │
│  │   5 faces  ★★☆☆☆   │  ⬡         │  └───┘ └───┘        │   │
│  │                     │            └─────────────────────┘   │
│  │ 👤 Carol Davis      │                                       │
│  │   12 faces ★★★★☆   │                                       │
│  │                     │                                       │
│  └─────────────────────┘                                       │
│                                                                 │
│  [Face Settings]                                                │
│  Auto-assign threshold: [──────────●────────────────] 0.85      │
│  Suggest threshold:     [────────────────●────────] 0.70       │
└─────────────────────────────────────────────────────────────────┘
```

**Person detail view (selecting a person):**
```
┌─ Alice Johnson ─────────────────────────────────────[✕ Close]─┐
│                                                                 │
│  Rename: [Alice Johnson_______________]  [Save]                │
│  Notes: [Family photos from the_______]                         │
│         [2024 reunion                 ]                         │
│                                                                 │
│  Faces (23)                                                     │
│  ┌───┐ ┌───┐ ┌───┐ ┌───┐ ┌───┐ ┌───┐ ┌───┐ ┌───┐             │
│  │   │ │   │ │   │ │   │ │   │ │   │ │   │ │   │             │
│  └───┘ └───┘ └───┘ └───┘ └───┘ └───┘ └───┘ └───┘             │
│                                                                 │
│  All assets featuring Alice (5)                                 │
│  ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐                      │
│  │     │ │     │ │     │ │     │ │     │                       │
│  └─────┘ └─────┘ └─────┘ └─────┘ └─────┘                      │
│                                                                 │
│  [Merge with...]  [Delete Person]                               │
└─────────────────────────────────────────────────────────────────┘
```

**MainWindow integration:**
- Add "Faces" nav button in the title bar (next to "Metadata" position, or replace if Metadata was removed)
- Add `FaceTaggingViewModel → FaceTaggingView` DataTemplate
- Add `FaceTaggingViewModel` to DI and inject into `MainWindowViewModel`

**Keyboard shortcuts:**
- `N` — Name selected face
- `Enter` — Confirm suggestion
- `Esc` — Close detail/close view
- `Ctrl+F` — Focus search input

**Estimated LOC:** ~300

---

### T23.8 — UI: Person Management View

**Goal:** A dialog/popup for full person management: rename, merge duplicates, delete persons.

**Files:**
- `src/Adam.CatalogBrowser/ViewModels/PersonManagementViewModel.cs` (new)
- `src/Adam.CatalogBrowser/Views/PersonManagementView.axaml` (new)
- `src/Adam.CatalogBrowser/Views/PersonManagementView.axaml.cs` (new)

**PersonManagementViewModel:**
```csharp
public sealed class PersonManagementViewModel : INotifyPropertyChanged
{
    public IReadOnlyList<PersonItem> Persons { get; }
    public PersonItem? SelectedPerson { get; set; }
    public string? EditName { get; set; }
    public string? EditNotes { get; set; }
    public PersonItem? MergeTarget { get; set; }

    public ICommand RenameCommand { get; }
    public ICommand MergeCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenGalleryCommand { get; }
}

public sealed record PersonItem
{
    public Guid Id { get; init; }
    public string Name { get; init; }
    public byte[]? Thumbnail { get; init; }
    public int FaceCount { get; init; }
    public double AvgConfidence { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastSeenAt { get; init; }
}
```

**Merge dialog:** Two-column layout. Left: list of all persons. Right: selected merge target. "Merge" button moves all faces from source to target and deletes source.

**Delete confirmation:** "Delete Person 'Alice Johnson'? This will unlink 23 face records but won't delete the source assets."

**Estimated LOC:** ~200

---

### T23.9 — UI: Face Badge Overlay (Gallery Tiles)

**Goal:** Show face count badges on gallery thumbnails so users can see which assets have detected faces at a glance.

**Files:**
- `src/Adam.CatalogBrowser/Controls/FaceBadgeTile.cs` (new)
- `src/Adam.CatalogBrowser/Controls/Themes/FaceBadgeTileStyles.axaml` (new)
- `src/Adam.CatalogBrowser/Controls/AssetTileControl.cs` (integrate)

**FaceBadgeTile control:**
- Small overlay badge in the top-right corner of gallery thumbnails
- Shows face icon + count (e.g., "👤 3" or "🙂 1")
- Colors: blue for named persons, gray for unknown faces only, orange for mixed
- Clicking the badge opens the Face Tagging View filtered to that asset
- Only visible when asset has at least one `AssetFace` record

**Integration into AssetTileControl:**
- Add `HasFaces` boolean property (loaded in `AssetListItem`)
- Add `FaceCount` property
- Conditionally show the badge overlay when `FaceCount > 0`

**Estimated LOC:** ~80

---

### T23.10 — Tests

**Goal:** Comprehensive test coverage for all new services and ViewModels.

**Files:**
- `tests/Adam.Shared.Tests/Services/FaceDetectionServiceTests.cs` (new)
- `tests/Adam.Shared.Tests/Services/FaceRecognitionServiceTests.cs` (new)
- `tests/Adam.Shared.Tests/Services/FaceMatcherServiceTests.cs` (new)
- `tests/Adam.Shared.Tests/Services/FaceAlignerTests.cs` (new)
- `tests/Adam.CatalogBrowser.Tests/ViewModels/FaceTaggingViewModelTests.cs` (new)
- `tests/Adam.CatalogBrowser.Tests/ViewModels/PersonManagementViewModelTests.cs` (new)
- `tests/Adam.BrokerService.Tests/Handlers/FaceHandlerTests.cs` (new)
- `tests/Adam.BrokerService.Tests/Handlers/PersonHandlerTests.cs` (new)

**Test coverage:**

| Test File | Key Tests |
|-----------|-----------|
| `FaceDetectionServiceTests` | `DetectFaces_WithValidImage_ReturnsFaces`, `DetectFaces_NoFaces_ReturnsEmpty`, `DetectFaces_ImageCorrupt_Throws` |
| `FaceRecognitionServiceTests` | `GetFaceEmbedding_Returns512Dim`, `GetFaceEmbedding_SameCrop_Consistent`, `GetFaceEmbedding_DifferentCrop_Different` |
| `FaceMatcherServiceTests` | `MatchAsync_ExactMatch_AutoAssigns`, `MatchAsync_Partial_Suggests`, `MatchAsync_NoMatch_Unknown`, `ClusterUnknownFaces_ReturnsClusters`, `BatchMatchAsync_MatchesAll`, `ComputeCentroid_AveragesVectors` |
| `FaceAlignerTests` | `AlignFace_Returns112x112`, `AlignFace_SameOrientation_Consistent` |
| `FaceTaggingViewModelTests` | `Constructor_LoadsPersons`, `NameFace_CreatesPerson`, `SuggestNames_RunsClustering`, `ConfirmFace_UpdatesMatchType` |
| `PersonManagementViewModelTests` | `Rename_UpdatesName`, `Merge_MovesFaces`, `Delete_RemovesPerson`, `OpenGallery_FiltersAssets` |
| `FaceHandlerTests` | `DetectFaces_ValidAsset_ReturnsFaces`, `DetectFaces_NonImage_ReturnsError` |
| `PersonHandlerTests` | `ListPersons_ReturnsAll`, `NamePerson_CreatesOrLinks`, `MergePersons_CombinesFaces`, `DeletePerson_RemovesAndUnlinks` |

**Estimated LOC:** ~450

## Execution Order (Waves)

### Wave 1 — Foundation Layer (Models + Layout + Shared Services)

**Goal:** Establish data models, FaceModelLayout, core services (FaceAligner, FaceMatcherService), DI registrations, and DB migration.

**Estimated time:** ~3-4 hours

| Step | File(s) | Description | LOC |
|------|---------|-------------|-----|
| 1.1 | `src/Adam.Shared/Models/Person.cs` | NEW. Entity with Id, Name, Notes, ThumbnailImage, CentroidEmbedding, EmbeddingModelVersion, CreatedAt, ModifiedAt. | ~25 |
| 1.2 | `src/Adam.Shared/Models/AssetFace.cs` | NEW. Entity with Id, AssetId, PersonId, FaceEmbedding (byte[2048]), BoundingBoxJson, DetectionConfidence, MatchingConfidence, IsAutoAssigned, ThumbnailImage. | ~30 |
| 1.3 | `src/Adam.Shared/Data/AppDbContext.cs` | Add `Persons` and `AssetFaces` DbSet properties. Add entity config for both: keys, property constraints (max lengths, required), indexes on Name (unique), AssetId, PersonId, DetectionConfidence, CreatedAt. ForeignKey relationships: AssetFace→DigitalAsset (Cascade delete), AssetFace→Person (SetNull). | ~50 |
| 1.4 | `src/Adam.Shared/Migrations/20260618_AddFacialRecognition.cs` | NEW. EF Core migration: create Persons and AssetFaces tables. Run `dotnet ef migrations add AddFacialRecognition` to generate. | ~50 |
| 1.5 | `src/LiquidVision.Core/Configuration/FaceModelLayout.cs` | NEW. Generalized model layout for single-file models. Static factory methods: `YuNet()` and `ArcFace()`. Each returns layout with BaseUrl, ModelDirectory, LocalOnnxPath, RemoteFiles list. Caches under `%LOCALAPPDATA%/Adam/models/face/{model}/`. | ~80 |
| 1.6 | `src/Adam.Shared/Services/FaceAligner.cs` | NEW. `AlignFace(byte[] imageData, DetectedFace face) → byte[112*112*3]`. Uses SkiaSharp for decode, resize, affine transform with 5-landmark alignment. `ExtractThumbnail(byte[], size) → byte[]` for UI preview. | ~100 |
| 1.7 | `src/Adam.Shared/Services/FaceMatcherService.cs` | NEW. Core matching: `MatchAsync`, `BatchMatchAsync`, `ClusterUnknownFacesAsync`, `ComputeCentroidAsync`, `CosineSimilarity`. HDBSCAN-style connected components clustering. Uses `IDbContextFactory<AppDbContext>`. | ~200 |
| 1.8 | `src/Adam.Shared/Services/FaceDetectionPipelineService.cs` | NEW. Orchestrates the post-AI-tagging face detection flow. `ProcessAssetsAsync(assetIds, progress, ct)`: for each image asset, download + align → embed → match → store. | ~70 |
| 1.9 | `src/Adam.CatalogBrowser/App.axaml.cs` | Register `FaceDetectionService`, `FaceRecognitionService`, `FaceAligner`, `FaceMatcherService`, `FaceDetectionPipelineService`, `FaceTaggingViewModel`, `PersonManagementViewModel` in DI. | ~10 |
| 1.10 | `src/Adam.BrokerService/Program.cs` | Register `FaceDetectionService`, `FaceAligner`, `FaceMatcherService` in DI (for multi-user broker mode). | ~6 |

**Verification:** `dotnet build` — all projects compile without errors.

---

### Wave 2 — ONNX Services (Face Detection + Recognition)

**Goal:** Implement the YuNet and ArcFace ONNX inference services. Includes download, initialization, inference, and output decoding.

**Estimated time:** ~3-4 hours

| Step | File(s) | Description | LOC |
|------|---------|-------------|-----|
| 2.1 | `src/LiquidVision.Core/Interfaces/IFaceService.cs` | NEW. Interface with `InitializeAsync(IProgress<double>?, CancellationToken)`, `IsInitialized`, `DownloadProgress`. Common pattern matching `ILiquidVisionAnalyzer`. | ~15 |
| 2.2 | `src/LiquidVision.Core/Services/FaceDetectionService.cs` | NEW. Implements IFaceService. Constructor takes `FaceModelLayout` + `LiquidVisionOptions`. `InitializeAsync`: creates `ModelDownloader`, downloads YuNet ONNX, creates `InferenceSession`. `DetectFacesAsync(byte[] imageData)`: SkiaSharp decode → resize to 320×320 → NCHW BGR tensor → Run inference → Decode raw tensors to bounding boxes + 5 landmarks → Confidence filter (>0.5) → NMS (IoU 0.3). YuNet output decoding: anchor grid generation, bounding box transformation from center-offset to corner coordinates, landmark scaling from grid to image coordinates. | ~150 |
| 2.3 | `src/LiquidVision.Core/Services/FaceRecognitionService.cs` | NEW. Implements IFaceService. Constructor takes `FaceModelLayout` + `LiquidVisionOptions`. `InitializeAsync`: downloads ArcFace ONNX, creates `InferenceSession`. `GetFaceEmbeddingAsync(byte[] alignedFaceCrop)`: decodes with SkiaSharp, resizes to 112×112, normalizes `(pixels - 127.5) / 128.0`, creates NHWC tensor (1,112,112,3), runs inference, extracts 512-dim float32 output. Tensor name detected dynamically. | ~100 |

**Verification:** `dotnet build` — all projects compile without errors. Unit tests with mock ONNX output.

---

### Wave 3 — Broker Handlers & Multi-User (T23.5)

**Goal:** Multi-user message contracts and broker handlers for face/person operations.

**Estimated time:** ~1-2 hours

| Step | File(s) | Description | LOC |
|------|---------|-------------|-----|
| 3.1 | `src/Adam.Shared/Contracts/MessageTypeCode.cs` | Add 10 new opcodes in 170-179 range: DetectFacesRequest/Response, NamePersonRequest/Response, ListPersonsRequest/Response, MergePersonsRequest/Response, DeletePersonRequest/Response. | ~12 |
| 3.2 | `src/Adam.Shared/Contracts/FaceMessages.cs` | NEW. Protobuf contracts for all 10 messages following existing `IProtoSerializable` pattern with `[ProtoMember]` attributes. | ~100 |
| 3.3 | `src/Adam.BrokerService/Handlers/FaceHandler.cs` | NEW. `DetectFacesAsync`: loads asset → verifies image type → runs YuNet detection → ArcFace embedding → FaceMatcher matching → creates AssetFace records → returns count. | ~80 |
| 3.4 | `src/Adam.BrokerService/Handlers/PersonHandler.cs` | NEW. `ListPersonsAsync`, `NamePersonAsync` (creates new Person or links to existing), `MergePersonsAsync` (moves faces + recomputes centroid), `DeletePersonAsync` (unlinks faces + removes). | ~80 |
| 3.5 | `src/Adam.BrokerService/Handlers/ConnectionHandler.cs` | Map 5 new opcodes to handler methods. Inject `FaceHandler` and `PersonHandler`. | ~10 |

**Verification:** `dotnet build` — all projects compile without errors.

---

### Wave 4 — Face Tagging UI (T23.7)

**Goal:** Full face tagging view with person grouping, naming, confirmation, and unknown face management.

**Estimated time:** ~3-4 hours

| Step | File(s) | Description | LOC |
|------|---------|-------------|-----|
| 4.1 | `src/Adam.CatalogBrowser/ViewModels/FaceTaggingViewModel.cs` | NEW. State: Persons list, UnknownFaces list, SelectedPerson, IsLoading. Commands: NameFaceCommand, SuggestNamesCommand, ConfirmFaceCommand, RejectFaceCommand, RefreshCommand, OpenPersonGalleryCommand. Uses FaceMatcherService and DB queries. | ~140 |
| 4.2 | `src/Adam.CatalogBrowser/Views/FaceTaggingView.axaml` | NEW. XAML layout: two-column (Known Persons | Unknown Faces). Person cards with avatar+name+count+quality. Unknown faces grid with inline naming. Settings panel for confidence thresholds. | ~100 |
| 4.3 | `src/Adam.CatalogBrowser/Views/FaceTaggingView.axaml.cs` | NEW. Code-behind with keyboard shortcuts (N=name, Enter=confirm, Esc=close). | ~15 |
| 4.4 | `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` | Add `ShowFacesCommand` ICommand property + `_faceTaggingVm` field. Add nav bar button handler to switch to FaceTaggingView. | ~25 |
| 4.5 | `src/Adam.CatalogBrowser/Views/MainWindow.axaml` | Add DataTemplate mapping `FaceTaggingViewModel → FaceTaggingView` in Window.DataTemplates. Add "Faces" nav button. | ~5 |

**Verification:** `dotnet build` — all projects compile.

---

### Wave 5 — Person Management UI + Face Badge (T23.8 + T23.9)

**Goal:** Person management dialog and face badge overlays on gallery tiles.

**Estimated time:** ~2-3 hours

| Step | File(s) | Description | LOC |
|------|---------|-------------|-----|
| 5.1 | `src/Adam.CatalogBrowser/ViewModels/PersonManagementViewModel.cs` | NEW. Persons list, rename, merge (two-column target selector), delete with confirmation, open person gallery. | ~100 |
| 5.2 | `src/Adam.CatalogBrowser/Views/PersonManagementView.axaml` | NEW. Dialog layout: list view with search, rename inline, merge target selector, delete button. | ~60 |
| 5.3 | `src/Adam.CatalogBrowser/Views/PersonManagementView.axaml.cs` | NEW. Keyboard shortcuts. | ~10 |
| 5.4 | `src/Adam.CatalogBrowser/Controls/FaceBadgeTile.cs` | NEW. Small overlay control showing face icon + count. Auto-hides when count=0. Clickable to open face view. | ~40 |
| 5.5 | `src/Adam.CatalogBrowser/Controls/Themes/FaceBadgeTileStyles.axaml` | NEW. Styled overlay with rounded badge, conditional colors (blue=all named, gray=all unknown, orange=mixed). | ~30 |
| 5.6 | `src/Adam.CatalogBrowser/Controls/AssetTileControl.cs` | Add `HasFaces`/`FaceCount` properties. Conditionally show `FaceBadgeTile` in the control template. Load from DB or `AssetListItem` data. | ~10 |

**Verification:** `dotnet build` — all projects compile.

---

### Wave 6 — Tests & Integration (T23.10)

**Goal:** Full test suite, edge case hardening, code review, plan completion.

**Estimated time:** ~3-4 hours

| Step | Action | Description |
|------|--------|-------------|
| 6.1 | `tests/Adam.Shared.Tests/Services/FaceAlignerTests.cs` | NEW. 5+ tests. |
| 6.2 | `tests/Adam.Shared.Tests/Services/FaceMatcherServiceTests.cs` | NEW. 8+ tests covering match, cluster, centroid, batch. |
| 6.3 | `tests/Adam.CatalogBrowser.Tests/ViewModels/FaceTaggingViewModelTests.cs` | NEW. 6+ tests. |
| 6.4 | `tests/Adam.CatalogBrowser.Tests/ViewModels/PersonManagementViewModelTests.cs` | NEW. 5+ tests. |
| 6.5 | `tests/Adam.BrokerService.Tests/Handlers/FaceHandlerTests.cs` | NEW. 4+ tests. |
| 6.6 | `tests/Adam.BrokerService.Tests/Handlers/PersonHandlerTests.cs` | NEW. 4+ tests. |
| 6.7 | `dotnet test` | Run full test suite. All ~1,350 tests (existing + new) must pass. |
| 6.8 | `dotnet build` (all projects) | Verify no warnings or errors. |
| 6.9 | Edge case hardening | Check: no images with faces, no faces on non-image assets, no embeddings in DB, single-face assets, corrupted image files, cancellation during long pipelines. |
| 6.10 | Code review | Spawn `code-reviewer-deepseek-flash` to review all new/modified files. |
| 6.11 | Update STATE.md | Mark Phase 23 as complete. |

## Wave Dependency Graph

```
Wave 1 (Models + Layout + Shared Services) ────────────────────────────
      │                    │                    │
      ▼                    │                    │
Wave 2 (ONNX Services)     │                    │
      │                    │                    │
      ▼                    ▼                    │
Wave 3 (Broker Handlers)  Wave 4 (Face UI)     │
      │                    │                    │
      │                    ▼                    ▼
      │              Wave 5 (Person Mgmt + Badge)
      │                    │
      └────────────────────┼────────────────────┘
                           ▼
                    Wave 6 (Tests + Integration)
```

**Key dependency notes:**
- Wave 2 depends on Wave 1 (needs FaceModelLayout + FaceAligner)
- Wave 3 depends on Waves 1-2 (needs all shared services + ONNX services)
- Waves 4-5 depend on Wave 1 (need data models + FaceMatcherService)
- Wave 6 depends on all previous waves

## Parallelization Strategy

With 2-3 developers working in parallel:
- **Dev A:** Waves 1 → 2 → 3 (End-to-end: foundation, ONNX services, broker handlers)
- **Dev B:** Waves 1 → 4 → 5 (UI: face tagging view, person management, face badge)
- **All:** Wave 6 (tests and integration)

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| YuNet ONNX output decoding is complex (anchor grid + NMS) | Medium | High | Prototype in C# console app first; fall back to fixed 320×320 input if dynamic shapes cause issues; use `face_detection_yunet_2023mar.onnx` as fallback |
| ArcFace input tensor name varies between ONNX exports | Medium | Low | Detect dynamically with `session.InputMetadata.Keys.First()`; test against `garavv/arcface-onnx`'s `arc.onnx` during development |
| Face quality varies wildly in user catalogs | High | Low | Conservative detection thresholds (0.5 YuNet, 0.7 suggest); users can adjust |
| Face detection adds significant latency to AI tagging | Medium | Medium | YuNet runs in ~20ms on CPU; ArcFace in ~10ms; total ~30ms per face — negligible vs LFM2-VL (3-30s) |
| HDBSCAN clustering quality varies with diverse catalogs | Medium | Medium | Show cluster preview before creating persons; users can merge/split/rename; sensitivity controls |

## Files Created

| # | File | Task |
|---|------|------|
| 1 | `src/Adam.Shared/Models/Person.cs` | T23.3 |
| 2 | `src/Adam.Shared/Models/AssetFace.cs` | T23.3 |
| 3 | `src/Adam.Shared/Services/FaceAligner.cs` | T23.2 |
| 4 | `src/Adam.Shared/Services/FaceMatcherService.cs` | T23.4 |
| 5 | `src/Adam.Shared/Services/FaceDetectionPipelineService.cs` | T23.6 |
| 6 | `src/Adam.Shared/Contracts/FaceMessages.cs` | T23.5 |
| 7 | `src/LiquidVision.Core/Configuration/FaceModelLayout.cs` | T23.1 |
| 8 | `src/LiquidVision.Core/Interfaces/IFaceService.cs` | T23.1 |
| 9 | `src/LiquidVision.Core/Services/FaceDetectionService.cs` | T23.1 |
| 10 | `src/LiquidVision.Core/Services/FaceRecognitionService.cs` | T23.1 |
| 11 | `src/Adam.BrokerService/Handlers/FaceHandler.cs` | T23.5 |
| 12 | `src/Adam.BrokerService/Handlers/PersonHandler.cs` | T23.5 |
| 13 | `src/Adam.CatalogBrowser/ViewModels/FaceTaggingViewModel.cs` | T23.7 |
| 14 | `src/Adam.CatalogBrowser/Views/FaceTaggingView.axaml` | T23.7 |
| 15 | `src/Adam.CatalogBrowser/Views/FaceTaggingView.axaml.cs` | T23.7 |
| 16 | `src/Adam.CatalogBrowser/ViewModels/PersonManagementViewModel.cs` | T23.8 |
| 17 | `src/Adam.CatalogBrowser/Views/PersonManagementView.axaml` | T23.8 |
| 18 | `src/Adam.CatalogBrowser/Views/PersonManagementView.axaml.cs` | T23.8 |
| 19 | `src/Adam.CatalogBrowser/Controls/FaceBadgeTile.cs` | T23.9 |
| 20 | `src/Adam.CatalogBrowser/Controls/Themes/FaceBadgeTileStyles.axaml` | T23.9 |
| 21 | `src/Adam.Shared/Migrations/20260618_AddFacialRecognition.cs` | T23.3 |

## Files Modified

| # | File | Change |
|---|------|--------|
| 1 | `src/Adam.Shared/Data/AppDbContext.cs` | Add Persons + AssetFaces DbSets + entity config |
| 2 | `src/Adam.Shared/Contracts/MessageTypeCode.cs` | Add 10 new opcodes (170-179) |
| 3 | `src/Adam.CatalogBrowser/App.axaml.cs` | DI registration for face services |
| 4 | `src/Adam.BrokerService/Program.cs` | DI registration for face services |
| 5 | `src/Adam.BrokerService/Handlers/ConnectionHandler.cs` | Register FaceHandler + PersonHandler dispatcher entries |
| 6 | `src/Adam.CatalogBrowser/ViewModels/MainWindowViewModel.cs` | Add ShowFacesCommand, nav binding |
| 7 | `src/Adam.CatalogBrowser/Views/MainWindow.axaml` | Add DataTemplate + Faces nav button |
| 8 | `src/Adam.CatalogBrowser/Controls/AssetTileControl.cs` | Add HasFaces/FaceCount, show FaceBadgeTile |

## Estimated New Test Files

| # | File | Task |
|---|------|------|
| 1 | `tests/Adam.Shared.Tests/Services/FaceAlignerTests.cs` | T23.10 |
| 2 | `tests/Adam.Shared.Tests/Services/FaceMatcherServiceTests.cs` | T23.10 |
| 3 | `tests/Adam.CatalogBrowser.Tests/ViewModels/FaceTaggingViewModelTests.cs` | T23.10 |
| 4 | `tests/Adam.CatalogBrowser.Tests/ViewModels/PersonManagementViewModelTests.cs` | T23.10 |
| 5 | `tests/Adam.BrokerService.Tests/Handlers/FaceHandlerTests.cs` | T23.10 |
| 6 | `tests/Adam.BrokerService.Tests/Handlers/PersonHandlerTests.cs` | T23.10 |

## Success Criteria

### T23.1 — Face Model Infrastructure
- ✅ YuNet ONNX is downloaded and cached on first initialization
- ✅ ArcFace ONNX is downloaded and cached on first initialization
- ✅ FaceDetectionService runs YuNet inference on 320×320 images
- ✅ FaceRecognitionService runs ArcFace inference on 112×112 aligned crops
- ✅ Output decoding (anchor → bounding box + landmarks + NMS) produces correct results
- ✅ ModelDownloader retry/resume works for face models
- ✅ 5+ tests pass

### T23.2 — Face Alignment
- ✅ FaceAligner produces 112×112 RGB crops from full-image + detected face
- ✅ 5-landmark affine alignment normalizes roll/pitch/yaw
- ✅ Thumbnail extraction works (64×64)
- ✅ 2+ tests pass

### T23.3 — Data Models & DB
- ✅ Person table with Name, CentroidEmbedding, ThumbnailImage
- ✅ AssetFace table with FaceEmbedding, BoundingBoxJson, Confidence, IsAutoAssigned
- ✅ Foreign keys: AssetFace→DigitalAsset (Cascade), AssetFace→Person (SetNull)
- ✅ Composite index on (PersonId, AssetId)
- ✅ EF migration generated and tested

### T23.4 — Face Matching Service
- ✅ MatchAsync auto-assigns at >0.85 confidence
- ✅ MatchAsync suggests at 0.70–0.85 confidence
- ✅ BatchMatchAsync processes all unmatched faces
- ✅ ClusterUnknownFacesAsync groups unknown faces into HDBSCAN clusters
- ✅ ComputeCentroidAsync produces averaged unit-vector centroids
- ✅ 8+ tests pass

### T23.5 — Broker Messages & Handlers
- ✅ 10 new opcodes in MessageTypeCode.cs
- ✅ FaceMessages.cs with valid protobuf contracts
- ✅ FaceHandler.DetectFacesAsync returns face count
- ✅ PersonHandler accepts NamePerson, MergePersons, DeletePerson, ListPersons
- ✅ 4+ broker handler tests pass

### T23.6 — Pipeline Integration
- ✅ Face detection runs automatically on AI-tagged images
- ✅ Processing progress reported to status bar: "Detecting faces... (3/15)"
- ✅ Non-image assets silently skipped
- ✅ No performance regression in AI tagging pipeline

### T23.7 — Face Tagging View
- ✅ Known persons listed with face count + quality
- ✅ Unknown faces shown with inline naming
- ✅ Person detail: rename, notes, face grid, asset grid
- ✅ "Suggest Names" button invokes HDBSCAN clustering
- ✅ Confirmation workflow for suggested matches (0.70–0.85)
- ✅ Confidence threshold controls
- ✅ 6+ ViewModel tests pass

### T23.8 — Person Management
- ✅ Full person list with search
- ✅ Rename person inline
- ✅ Merge two persons (moves faces, recomputes centroid, deletes source)
- ✅ Delete person with confirmation (unlinks faces, doesn't delete assets)
- ✅ Open gallery filtered to person's assets
- ✅ 5+ ViewModel tests pass

### T23.9 — Face Badge Overlay
- ✅ Face badge visible on gallery tiles with detected faces
- ✅ Badge shows face count + icon
- ✅ Color-coded: blue (all named), gray (all unknown), orange (mixed)
- ✅ Click opens face tagging view

### Overall (Phase 23)
- ✅ All ~1,350 existing tests still pass
- ✅ No regressions in existing AI tagging, gallery, or search features
- ✅ Code review passed — no HIGH/MEDIUM concerns
- ✅ File streaming for face thumbnails verified
