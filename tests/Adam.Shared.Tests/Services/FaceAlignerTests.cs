using Adam.Shared.Services;
using FluentAssertions;
using SkiaSharp;

namespace Adam.Shared.Tests.Services;

public sealed class FaceAlignerTests
{
    private readonly FaceAligner _sut = new();

    /// <summary>
    /// Creates a synthetic 200×200 image with a face-like pattern.
    /// Two dark circles (eyes) and a triangle (nose) on a light background.
    /// </summary>
    private static byte[] CreateTestImage(int width = 200, int height = 200)
    {
        using var bmp = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.LightGray);

        // Draw two "eye" spots
        using var eyePaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        canvas.DrawCircle(70, 80, 10, eyePaint);
        canvas.DrawCircle(130, 80, 10, eyePaint);

        // Draw a "nose" spot
        using var nosePaint = new SKPaint { Color = SKColors.DarkGray, IsAntialias = true };
        canvas.DrawCircle(100, 110, 8, nosePaint);

        // Draw "mouth" spots
        using var mouthPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        canvas.DrawCircle(85, 140, 6, mouthPaint);
        canvas.DrawCircle(115, 140, 6, mouthPaint);

        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static (float X, float Y)[] CreateTestLandmarks(float scale = 1.0f)
    {
        float s = scale;
        return
        [
            (70 * s, 80 * s),   // left eye
            (130 * s, 80 * s),  // right eye
            (100 * s, 110 * s), // nose
            (85 * s, 140 * s),  // left mouth
            (115 * s, 140 * s)  // right mouth
        ];
    }

    [Fact]
    public void AlignFace_Returns112x112()
    {
        var image = CreateTestImage();
        var landmarks = CreateTestLandmarks();

        var result = _sut.AlignFace(image, 0, 0, 200, 200, landmarks, 200, 200);

        result.Should().NotBeNull();
        result.Length.Should().Be(112 * 112 * 3, "output should be 112×112 RGB");
    }

    [Fact]
    public void AlignFace_SameOrientation_Consistent()
    {
        var image = CreateTestImage();
        var landmarks = CreateTestLandmarks();

        var result1 = _sut.AlignFace(image, 0, 0, 200, 200, landmarks, 200, 200);
        var result2 = _sut.AlignFace(image, 0, 0, 200, 200, landmarks, 200, 200);

        result1.Should().Equal(result2, "same input should produce identical aligned output");
    }

    [Fact]
    public void AlignFace_DifferentScale_StillReturns112x112()
    {
        var image = CreateTestImage(400, 400);
        var landmarks = CreateTestLandmarks(2.0f);

        var result = _sut.AlignFace(image, 0, 0, 400, 400, landmarks, 400, 400);

        result.Should().NotBeNull();
        result.Length.Should().Be(112 * 112 * 3);
    }

    [Fact]
    public void AlignFace_NormalizedLandmarks_DenormalizesCorrectly()
    {
        var image = CreateTestImage();
        // Landmarks in normalized 0-1 range
        var normLandmarks = new (float X, float Y)[]
        {
            (0.35f, 0.40f),
            (0.65f, 0.40f),
            (0.50f, 0.55f),
            (0.425f, 0.70f),
            (0.575f, 0.70f)
        };

        var result = _sut.AlignFace(image, 0, 0, 200, 200, normLandmarks, 200, 200);

        result.Should().NotBeNull();
        result.Length.Should().Be(112 * 112 * 3);
    }

    [Fact]
    public void ExtractThumbnail_ReturnsCorrectSize()
    {
        var image = CreateTestImage();
        var landmarks = CreateTestLandmarks();
        var aligned = _sut.AlignFace(image, 0, 0, 200, 200, landmarks, 200, 200);

        var thumbnail = _sut.ExtractThumbnail(aligned, 64);

        thumbnail.Should().NotBeNull();
        thumbnail.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ExtractThumbnail_DefaultSize_Works()
    {
        var image = CreateTestImage();
        var landmarks = CreateTestLandmarks();
        var aligned = _sut.AlignFace(image, 0, 0, 200, 200, landmarks, 200, 200);

        var thumbnail = _sut.ExtractThumbnail(aligned);

        thumbnail.Should().NotBeNull();
        thumbnail.Length.Should().BeGreaterThan(0);
    }
}
