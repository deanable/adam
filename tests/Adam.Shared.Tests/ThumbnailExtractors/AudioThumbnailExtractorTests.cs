using System;
using System.IO;
using System.Threading.Tasks;
using Adam.Shared.ThumbnailExtractors;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using TagLib;
using Xunit;

namespace Adam.Shared.Tests.ThumbnailExtractors;

public class AudioThumbnailExtractorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AudioThumbnailExtractor _sut = new();

    public AudioThumbnailExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void CanExtract_Mp3_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/song.mp3", "audio/mpeg").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Flac_ReturnsTrue()
    {
        _sut.CanExtract("/tmp/song.flac", "audio/flac").Should().BeTrue();
    }

    [Fact]
    public void CanExtract_Jpg_ReturnsFalse()
    {
        _sut.CanExtract("/tmp/photo.jpg", "image/jpeg").Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_Mp3WithAlbumArt_ReturnsTrue()
    {
        var source = Path.Combine(_tempDir, "with_art.mp3");
        var dest = Path.Combine(_tempDir, "thumb.jpg");

        CreateMp3WithAlbumArt(source);

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeTrue();
        System.IO.File.Exists(dest).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_Mp3WithoutAlbumArt_ReturnsFalse()
    {
        var source = Path.Combine(_tempDir, "no_art.mp3");
        var dest = Path.Combine(_tempDir, "thumb.jpg");

        CreateMinimalMp3(source);

        var result = await _sut.ExtractAsync(source, dest, 256, default);

        result.Should().BeFalse();
    }

    private static void CreateMp3WithAlbumArt(string path)
    {
        WriteMinimalMp3Frame(path);

        // Create a 1x1 red JPEG for the album art
        using var ms = new MemoryStream();
        using (var image = new Image<Rgba32>(1, 1, new Rgba32(255, 0, 0)))
        {
            image.Save(ms, new JpegEncoder());
        }
        var picData = ms.ToArray();

        var file = TagLib.File.Create(path);
        file.Tag.Pictures = new IPicture[]
        {
            new Picture(new ByteVector(picData, picData.Length))
            {
                Type = PictureType.FrontCover,
                MimeType = "image/jpeg"
            }
        };
        file.Save();
    }

    private static void CreateMinimalMp3(string path)
    {
        WriteMinimalMp3Frame(path);

        var file = TagLib.File.Create(path);
        file.Tag.Title = "Test";
        file.Save();
    }

    /// <summary>
    /// Writes a minimal valid MPEG-1 Layer-3 frame (128 kbps, 44.1 kHz, no padding)
    /// so TagLibSharp can open it without throwing CorruptFileException.
    /// </summary>
    private static void WriteMinimalMp3Frame(string path)
    {
        // Frame header: sync word (0xFFE) + MPEG-1 (11) + Layer-3 (01) + no CRC (1)
        // + 128 kbps (1001) + 44.1 kHz (00) + no pad (0) + private (0)
        // + stereo (00) + no mode extension (00) + no copyright (0) + original (0) + no emphasis (00)
        // Result: FF FB 92 00
        const int FrameSize = 418; // (144 * 128000) / 44100 ≈ 418
        var frame = new byte[FrameSize];
        frame[0] = 0xFF;
        frame[1] = 0xFB;
        frame[2] = 0x92;
        frame[3] = 0x00;
        System.IO.File.WriteAllBytes(path, frame);
    }
}
