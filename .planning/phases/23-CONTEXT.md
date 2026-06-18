# Phase 23 — Facial Recognition (Discussion Context)

**Phase:** 23 | **Milestone:** v5.0 — AI-Native DAM
**Discussed:** 2026-06-18 | **Plan:** `.planning/plans/phase-23/23-PLAN.md` (pending)

## Scope

Phase 23 delivers **facial recognition** using a two-stage ONNX pipeline:

| Component | Model | Size | Function |
|-----------|-------|------|----------|
| Face Detection | **SCRFD-2.5G-KPS** (`2.5g_bnkps.onnx`) | ~3 MB | Detects faces + 5 landmarks (eyes, nose, mouth corners) |
| Face Recognition | **ArcFace** (`arc.onnx`, MobileFaceNet) | ~8 MB | Extracts 512-dim embedding from aligned face crop |

Total model download: **~11 MB** — far smaller than the LFM2-VL models (250 MB–3.2 GB).

## Pipeline

```
Image → SCRFD-2.5G-KPS → [Bounding Box + 5 Landmarks] → distance2bbox()/kps() + NMS
    → Affine Alignment → Crop 112x112
    → ArcFace → 512-dim Embedding → Compare vs Known Persons → Assign/Suggest/Flag
```

## Locked Decisions

### A. Pipeline Trigger
- ✅ **During AI tagging** — Face detection runs automatically as part of the existing `AiTaggingService.TagAssetsAsync` pipeline. After the AI tagging result is obtained, the face recognition pipeline processes each tagged image asset.
- No separate "Detect Faces" button initially (can be added later).

### B. Face Scope
- ✅ **Only AI-tagged assets** — Only assets that have gone through the AI tagging pipeline get face detection. Non-tagged images are not processed.

### C. Matching Algorithm
- ✅ **HDBSCAN clustering** — Unknown faces are grouped into candidate person clusters (same `EmbeddingClusterService` pattern from Phase 22). The user can then name, merge, or dismiss the clusters.
- Nearest-neighbor fallback: once persons are established, new faces are matched against known person centroids.

### D. Confidence Thresholds
- ✅ **Auto-assign:** >0.85 — Person name assigned automatically, no confirmation needed
- ✅ **Suggest:** 0.70–0.85 — Flagged for review, user must confirm or reject
- ✅ **Unknown:** <0.70 — Treated as a new/unknown person candidate for clustering

### E. Model Source
- ✅ **HuggingFace hub (primary) + InsightFace GitHub (secondary)** — ArcFace from HuggingFace (`garavv/arcface-onnx`), SCRFD from HuggingFace community mirror (`RuteNL/SCRFD-face-detection-ONNX`) with InsightFace official as fallback. Both use the `ModelDownloader`/`ModelCacheManager` pattern. Models cached under `%LOCALAPPDATA%/Adam/models/face/`.

#### Specific Repositories

**ArcFace:** [`garavv/arcface-onnx`](https://huggingface.co/garavv/arcface-onnx) on HuggingFace
- Model file: `arc.onnx` (~8 MB)
- Download URL: `https://huggingface.co/garavv/arcface-onnx/resolve/main/arc.onnx?download=true`
- Input shape: `(1, 112, 112, 3)` — NHWC, RGB (uint8 → float32 normalized)
- Output: 512-dim float32 embedding vector
- Preprocessing: `(pixels - 127.5) / 128.0`
- Input tensor name: dynamic (use `session.InputMetadata.Keys.First()`)

**SCRFD-2.5G-KPS (Face Detection):** [`RuteNL/SCRFD-face-detection-ONNX`](https://huggingface.co/RuteNL/SCRFD-face-detection-ONNX) on HuggingFace (community mirror) / [InsightFace GitHub](https://github.com/deepinsight/insightface/blob/master/detection/scrfd/README.md#pretrained-models) (official)
- Model file: `2.5g_bnkps.onnx` (~3 MB)
- HuggingFace download URL: `https://huggingface.co/RuteNL/SCRFD-face-detection-ONNX/resolve/main/2.5g_bnkps.onnx`
- Official fallback: InsightFace README's Pretrained-Models table ("SCRFD_2.5G_KPS" row, Google Drive link)
- Input shape: `(1, 3, H, W)` — NCHW, BGR (dynamic H/W, default 640×640)
- Output: 3-scale raw distance tensors — `scores` (1,2,H/s,W/s), `bbox_preds` (1,4,H/s,W/s), `kps_preds` (1,10,H/s,W/s) — requires `distance2bbox()` + `distance2kps()` conversion + NMS
- C# post-processing: ~110 LOC (distance decoding + confidence filter + NMS — simpler than YuNet's anchor-based approach)
- Inference: ~4.3ms on CPU at VGA resolution, 0.82M params, 2.5G FLOPs
- WIDER Face Hard: 77.13 (with landmarks) — compares favorably to YuNet

**Alternative larger variant (evaluated):** SCRFD-10G-KPS (`10g_bnkps.onnx`)
- Higher accuracy (82.80 WIDER Face Hard) but 4.23M params with 10G FLOPs
- Download: `https://huggingface.co/RuteNL/SCRFD-face-detection-ONNX/resolve/main/10g_bnkps.onnx`
- Not chosen: overkill for DAM use case; 2.5G-KPS accuracy is sufficient for catalog face detection

**Alternative YOLOv8n-face (evaluated but not chosen):**
- Pre-exported from `deepghs/yolo-face` (12.1 MB) — raw output `(1,5,8400)` requires box decode + NMS
- Could be exported with `nms=True` for zero post-processing but requires Python build step
- 36× larger than SCRFD-2.5G-KPS with comparable accuracy
- SCRFD's distance-based decoding is more efficient for the C# pipeline

### F. Person Management Scope
- ✅ **Full person management** — List view, merge duplicates, rename, delete, view all assets containing a specific person.

### G. Data Models

```csharp
// New entities:
Person  — Id, Name, Notes, ThumbnailImage, CreatedAt, ModifiedAt, Faces (nav)
AssetFace — Id, AssetId, PersonId?, FaceEmbedding (byte[2048]), BoundingBox (JSON),
            Confidence, IsAutoAssigned, ThumbnailImage, Asset, Person
```

### H. Multi-User Support
- ✅ Broker messages: `DetectFacesRequest/Response`, `NamePersonRequest/Response`
- ✅ `FaceHandler` + `PersonHandler` on broker side
- ✅ Face embeddings in shared DB, accessible to all connected users

### I. UI Components

| Component | Type | Description |
|-----------|------|-------------|
| FaceTaggingView | Full view | Browse faces detected across the catalog, grouped by person/unknown |
| PersonManagementView | Dialog/View | List, merge, rename, delete persons |
| FaceBadgeTile | Control | Overlay on gallery tiles showing face count/thumbnails |

## Open Items (Technical)

- **Model download infrastructure** — The existing `Lfm2VlModelLayout`/`ModelDownloader` is specific to LFM2-VL. Need a generalized model layout system or a separate `FaceModelLayout` for SCRFD + ArcFace.
- **SCRFD ONNX post-processing** — ~110 LOC C# needed for `distance2bbox()`/`distance2kps()` conversion, multi-scale output aggregation, confidence filtering, and NMS. This is the highest-risk item but significantly simpler than YuNet's anchor-based approach.
- **Face alignment** — Need a C# affine transform function to warp the 5 SCRFD landmarks into aligned 112×112 crops for ArcFace. `SixLabors.ImageSharp` has `ProjectiveTransformProcessor` (already a dependency of LiquidVision.Core).
- **ArcFace input tensor name** — `garavv/arcface-onnx`'s `arc.onnx` may have a non-standard input name. Must detect dynamically with `session.InputMetadata.Keys.First()`. Test needed during development.
- **SCRFD model source reliability** — The RuteNL HuggingFace mirror is a community upload, not official. The official InsightFace README links to Google Drive files which may have rate limiting. Should implement dual-source download with fallback.

## Next Steps

1. ✅ Phase 23 discussed — decisions locked in `23-CONTEXT.md`
2. ✅ Model repositories researched and documented — `garavv/arcface-onnx` (HuggingFace) + `RuteNL/SCRFD-face-detection-ONNX` (HuggingFace) with InsightFace official fallback
3. 🔜 Run `/gsd-discuss-phase 23` or `/gsd-plan-phase 23` to create the detailed execution plan
4. 🔜 Run `/gsd-execute-phase 23` to begin implementation

## Questions Resolved During Discussion

| Question | Decision |
|----------|----------|
| Run face detection during AI tagging or on-demand? | ✅ During AI tagging |
| Nearest-neighbor or HDBSCAN clustering for face matching? | ✅ HDBSCAN clustering |
| Model source strategy? | ✅ HuggingFace hub (ArcFace + SCRFD) with InsightFace GitHub fallback |
| Face detection model? | ✅ SCRFD-2.5G-KPS from `RuteNL/SCRFD-face-detection-ONNX` |
| Confidence thresholds for auto-assignment? | ✅ Auto >0.85, Suggest 0.70-0.85, Unknown <0.70 |
| Full person management view or basic naming only? | ✅ Full person management |
| All images or only AI-tagged assets? | ✅ Only AI-tagged assets |
