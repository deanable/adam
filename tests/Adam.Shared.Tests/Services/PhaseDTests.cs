using FluentAssertions;
using Adam.Shared.Services;
using Adam.Shared.Validation;
using Adam.Shared.Models;

namespace Adam.Shared.Tests.Services;

public class PhaseDTests
{
    // ===== AssetValidator =====

    public class AssetValidatorTests
    {
        private readonly AssetValidator _sut = new();

        [Fact]
        public void ValidateForIngestion_ValidJpeg_ReturnsValid()
        {
            var result = _sut.ValidateForIngestion("photo.jpg", 1024, "My Photo", ["tag1"], "A description");
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void ValidateForIngestion_InvalidExtension_ReturnsError()
        {
            var result = _sut.ValidateForIngestion("file.exe", 1024, "Title", [], null);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("not supported"));
        }

        [Fact]
        public void ValidateForIngestion_TooLarge_ReturnsError()
        {
            var result = _sut.ValidateForIngestion("photo.jpg", 3L * 1024 * 1024 * 1024, "Title", [], null);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("2 GB"));
        }

        [Fact]
        public void ValidateForIngestion_TitleTooLong_ReturnsError()
        {
            var result = _sut.ValidateForIngestion("photo.jpg", 1024, new string('x', 201), [], null);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("200"));
        }

        [Fact]
        public void ValidateForIngestion_TooManyTags_ReturnsError()
        {
            var tags = Enumerable.Range(0, 21).Select(i => $"tag{i}").ToArray();
            var result = _sut.ValidateForIngestion("photo.jpg", 1024, "Title", tags, null);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("20"));
        }

        [Fact]
        public void ValidateForIngestion_DescriptionTooLong_ReturnsError()
        {
            var desc = new string('x', 2001);
            var result = _sut.ValidateForIngestion("photo.jpg", 1024, "Title", [], desc);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("2000"));
        }

        [Fact]
        public void ValidateForIngestion_EmptyTitle_ReturnsError()
        {
            var result = _sut.ValidateForIngestion("photo.jpg", 1024, "", [], null);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("Title is required"));
        }

        [Theory]
        [InlineData(".jpg")]
        [InlineData(".jpeg")]
        [InlineData(".png")]
        [InlineData(".webp")]
        [InlineData(".cr2")]
        [InlineData(".nef")]
        [InlineData(".pdf")]
        [InlineData(".mp4")]
        [InlineData(".mp3")]
        public void ValidateForIngestion_SupportedExtensions_Pass(string ext)
        {
            var result = _sut.ValidateForIngestion($"file{ext}", 1024, "Title", [], null);
            result.IsValid.Should().BeTrue();
        }
    }

    // ===== ThumbnailService =====

    public class ThumbnailServiceTests
    {
        private readonly ThumbnailService _sut = new();

        [Fact]
        public void GetThumbnailPath_ReturnsDeterministicPath()
        {
            var path1 = _sut.GetThumbnailPath(@"C:\photo.jpg", @"C:\thumbnails");
            var path2 = _sut.GetThumbnailPath(@"C:\photo.jpg", @"C:\thumbnails");
            path1.Should().Be(path2);
            path1.Should().StartWith(Path.Combine(@"C:\thumbnails", ""));
            path1.Should().EndWith(".jpg");
        }

        [Fact]
        public void GetThumbnailPath_DifferentSources_DifferentPaths()
        {
            var path1 = _sut.GetThumbnailPath(@"C:\photo1.jpg", @"C:\thumbnails");
            var path2 = _sut.GetThumbnailPath(@"C:\photo2.jpg", @"C:\thumbnails");
            path1.Should().NotBe(path2);
        }
    }

    // ===== MetadataWritebackService =====

    public class MetadataWritebackServiceTests
    {
        private readonly MetadataWritebackService _sut = new();

        [Theory]
        [InlineData("photo.jpg")]
        [InlineData("photo.jpeg")]
        [InlineData("photo.tiff")]
        [InlineData("photo.tif")]
        [InlineData("photo.png")]
        [InlineData("photo.webp")]
        public void SupportsEmbeddedMetadata_SupportedExtensions_ReturnsTrue(string path)
        {
            _sut.SupportsEmbeddedMetadata(path).Should().BeTrue();
        }

        [Theory]
        [InlineData("photo.cr2")]
        [InlineData("photo.nef")]
        [InlineData("photo.arw")]
        [InlineData("photo.dng")]
        [InlineData("photo.gif")]
        [InlineData("photo.bmp")]
        public void SupportsEmbeddedMetadata_UnsupportedExtensions_ReturnsFalse(string path)
        {
            _sut.SupportsEmbeddedMetadata(path).Should().BeFalse();
        }

        [Theory]
        [InlineData("photo.cr2")]
        [InlineData("photo.nef")]
        [InlineData("photo.arw")]
        [InlineData("photo.dng")]
        public void IsRawFile_RawExtensions_ReturnsTrue(string path)
        {
            _sut.IsRawFile(path).Should().BeTrue();
        }

        [Theory]
        [InlineData("photo.jpg")]
        [InlineData("photo.png")]
        [InlineData("photo.tiff")]
        public void IsRawFile_NonRaw_ReturnsFalse(string path)
        {
            _sut.IsRawFile(path).Should().BeFalse();
        }

        [Fact]
        public async Task WriteSidecarXmpAsync_CreatesXmpFile()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var rawPath = Path.Combine(tempDir, "photo.nef");

            try
            {
                await File.WriteAllTextAsync(rawPath, "fake raw content");
                var profile = new MetadataProfile
                {
                    Creator = "Test Photographer",
                    Copyright = "\u00a9 2025 Test",
                    Headline = "Test Image",
                    Rating = 4,
                    Description = "A test image",
                    Title = "Test Photo",
                    City = "Portland",
                    State = "OR",
                    Country = "USA"
                };

                await _sut.WriteSidecarXmpAsync(rawPath, profile);

                var sidecarPath = Path.ChangeExtension(rawPath, ".xmp");
                File.Exists(sidecarPath).Should().BeTrue();
                var content = await File.ReadAllTextAsync(sidecarPath);
                content.Should().Contain("xpacket");
                content.Should().Contain("Test Photographer");
                content.Should().Contain("Test Image");
                content.Should().Contain("Portland");
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    // ===== ChecksumService =====

    public class ChecksumServiceTests
    {
        private readonly ChecksumService _sut = new();

        [Fact]
        public async Task ComputeSha256Async_ReturnsCorrectHash()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(tempFile, "hello world");
                var hash = await _sut.ComputeSha256Async(tempFile);
                hash.Should().NotBeNullOrEmpty();
                hash.Length.Should().Be(64);
                hash.Should().Be("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9");
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
    }
}
