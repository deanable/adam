using System;
using System.IO;
using SkiaSharp;

namespace Adam.Shared.Services;

/// <summary>
/// Aligns detected faces using 5-point landmark affine transformation
/// (YuNet output landmarks → canonical ArcFace input position).
/// Produces 112×112 RGB crops from full-resolution images.
/// </summary>
public sealed class FaceAligner
{
    // Canonical landmark positions for ArcFace alignment target (112×112 crop)
    private static readonly (float X, float Y)[] CanonicalLandmarks = new[]
    {
        (38.2946f, 51.6963f),  // left eye
        (73.5318f, 51.5014f),  // right eye
        (56.0252f, 71.7366f),  // nose tip
        (41.5493f, 92.3655f),  // left mouth corner
        (70.7299f, 92.2041f),  // right mouth corner
    };

    private const int TargetSize = 112;

    /// <summary>
    /// Aligns a face from the full image using YuNet's 5 landmarks.
    /// </summary>
    /// <param name="fullImageData">The original encoded image bytes (PNG/JPEG/etc.).</param>
    /// <param name="faceX">Bounding box left (normalized 0-1 or absolute).</param>
    /// <param name="faceY">Bounding box top (normalized 0-1 or absolute).</param>
    /// <param name="faceW">Bounding box width.</param>
    /// <param name="faceH">Bounding box height.</param>
    /// <param name="landmarks">5 facial landmarks as (X,Y) pairs.</param>
    /// <param name="imageWidth">Width of the full image in pixels (for denormalizing landmarks).</param>
    /// <param name="imageHeight">Height of the full image in pixels.</param>
    /// <returns>Aligned 112×112 RGB pixel data as byte[112*112*3] in NHWC layout.</returns>
    public byte[] AlignFace(
        byte[] fullImageData,
        float faceX, float faceY, float faceW, float faceH,
        (float X, float Y)[] landmarks,
        int imageWidth, int imageHeight)
    {
        using var bitmap = DecodeImage(fullImageData);

        // Denormalize landmarks if they are in normalized coordinates (0-1)
        var absLandmarks = new (float X, float Y)[landmarks.Length];
        bool isNormalized = landmarks[0].X <= 1.0f && landmarks[0].Y <= 1.0f;
        for (int i = 0; i < landmarks.Length; i++)
        {
            absLandmarks[i] = isNormalized
                ? (landmarks[i].X * imageWidth, landmarks[i].Y * imageHeight)
                : (landmarks[i].X, landmarks[i].Y);
        }

        // Compute affine transformation matrix from source landmarks to canonical positions
        var matrix = ComputeSimilarityTransform(absLandmarks, CanonicalLandmarks);

        // Apply affine transform with high-quality sampling
        using var aligned = new SKBitmap(TargetSize, TargetSize);
        using var canvas = new SKCanvas(aligned);
        canvas.Clear(SKColors.Black);
        canvas.SetMatrix(matrix);
        using var paint = new SKPaint
        {
            FilterQuality = SKFilterQuality.High,
            IsAntialias = true
        };
        canvas.DrawBitmap(bitmap, 0, 0, paint);
        canvas.ResetMatrix();

        // Extract RGB pixel data (NHWC layout)
        var pixels = aligned.Pixels;
        var rgb = new byte[TargetSize * TargetSize * 3];
        int idx = 0;
        for (int y = 0; y < TargetSize; y++)
        {
            for (int x = 0; x < TargetSize; x++)
            {
                var color = pixels[y * TargetSize + x];
                rgb[idx++] = color.Red;
                rgb[idx++] = color.Green;
                rgb[idx++] = color.Blue;
            }
        }

        return rgb;
    }

    /// <summary>
    /// Extracts a thumbnail from aligned face data for UI display.
    /// </summary>
    public byte[] ExtractThumbnail(byte[] alignedFace, int size = 64)
    {
        try
        {
            // Build bitmap from aligned face data using explicit pixel info
            var imageInfo = new SKImageInfo(TargetSize, TargetSize, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var bitmap = new SKBitmap(imageInfo);
            var pixels = bitmap.Pixels;
            if (pixels == null) return [];

            int idx = 0;
            for (int y = 0; y < TargetSize; y++)
            {
                for (int x = 0; x < TargetSize; x++)
                {
                    pixels[y * TargetSize + x] = new SKColor(
                        alignedFace[idx], alignedFace[idx + 1], alignedFace[idx + 2]);
                    idx += 3;
                }
            }
            // Pixels getter returns a copy; setter copies it back
            bitmap.Pixels = pixels;

            if (size != TargetSize)
            {
                using var resized = bitmap.Resize(
                    new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul),
                    new SKSamplingOptions(SKCubicResampler.Mitchell));
                if (resized != null)
                {
                    using var thumbData = resized.Encode(SKEncodedImageFormat.Jpeg, 85);
                    if (thumbData != null)
                        return thumbData.ToArray();
                }
            }

            using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 85);
            return data?.ToArray() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static SKBitmap DecodeImage(byte[] data)
    {
        var raw = SKBitmap.Decode(data)
            ?? throw new InvalidOperationException("Could not decode image data (unsupported or corrupt format).");

        if (raw.ColorType == SKColorType.Rgba8888 && raw.AlphaType == SKAlphaType.Unpremul)
            return raw;

        var converted = new SKBitmap(new SKImageInfo(raw.Width, raw.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (raw)
        {
            if (!raw.CopyTo(converted, SKColorType.Rgba8888))
                throw new InvalidOperationException("Could not convert image to RGBA.");
        }
        return converted;
    }

    /// <summary>
    /// Computes a 2×3 affine transformation matrix that maps source landmarks
    /// to canonical landmarks, minimizing least-squares error.
    /// </summary>
    private static SKMatrix ComputeSimilarityTransform(
        (float X, float Y)[] src, (float X, float Y)[] dst)
    {
        // Simple similarity transform using 2-point correspondence (eyes)
        // For production, implement full least-squares affine (6-DOF) using all 5 points.
        float srcDx = src[1].X - src[0].X;
        float srcDy = src[1].Y - src[0].Y;
        float dstDx = dst[1].X - dst[0].X;
        float dstDy = dst[1].Y - dst[0].Y;

        float srcNorm = MathF.Sqrt(srcDx * srcDx + srcDy * srcDy);
        float dstNorm = MathF.Sqrt(dstDx * dstDx + dstDy * dstDy);

        float scale = (srcNorm > 0) ? dstNorm / srcNorm : 1.0f;

        float angle = MathF.Atan2(dstDy, dstDx) - MathF.Atan2(srcDy, srcDx);

        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);

        float tx = dst[0].X - scale * (cos * src[0].X - sin * src[0].Y);
        float ty = dst[0].Y - scale * (sin * src[0].X + cos * src[0].Y);

        return new SKMatrix
        {
            ScaleX = cos * scale,
            SkewY = sin * scale,
            SkewX = -sin * scale,
            ScaleY = cos * scale,
            TransX = tx,
            TransY = ty,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1
        };
    }
}
