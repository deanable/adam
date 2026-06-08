using System.IO;
using FluentAssertions;
using LiquidVision.Core.Services;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Verifies <see cref="PreprocessorConfig.FromFile"/> reads both the flat <c>preprocessor_config.json</c>
/// (450M model) and the combined <c>processor_config.json</c> whose fields are nested under
/// <c>image_processor</c> (1.6B model).
/// </summary>
public sealed class PreprocessorConfigParsingTests
{
    [Fact]
    public void Reads_flat_preprocessor_config()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
            {
              "do_normalize": true,
              "patch_size": 16,
              "downsample_factor": 2,
              "max_num_patches": 1024,
              "image_mean": [0.5, 0.5, 0.5],
              "image_std": [0.5, 0.5, 0.5],
              "size": { "height": 512, "width": 512 }
            }
            """);

            var cfg = PreprocessorConfig.FromFile(path);

            cfg.PatchSize.Should().Be(16);
            cfg.DownsampleFactor.Should().Be(2);
            cfg.MaxNumPatches.Should().Be(1024);
            cfg.Height.Should().Be(512);
            cfg.ImageMean.Should().Equal(0.5f, 0.5f, 0.5f);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Reads_nested_processor_config_under_image_processor()
    {
        // Shape used by onnx-community/LFM2-VL-1.6B-ONNX/processor_config.json.
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
            {
              "image_processor": {
                "do_normalize": true,
                "patch_size": 16,
                "downsample_factor": 2,
                "max_image_tokens": 256,
                "min_image_tokens": 64,
                "max_num_patches": 1024,
                "rescale_factor": 0.00392156862745098,
                "image_mean": [0.5, 0.5, 0.5],
                "image_std": [0.5, 0.5, 0.5],
                "size": { "height": 512, "width": 512 }
              },
              "processor_class": "Lfm2VlProcessor"
            }
            """);

            var cfg = PreprocessorConfig.FromFile(path);

            cfg.PatchSize.Should().Be(16);
            cfg.DownsampleFactor.Should().Be(2);
            cfg.MaxImageTokens.Should().Be(256);
            cfg.MinImageTokens.Should().Be(64);
            cfg.MaxNumPatches.Should().Be(1024);
            cfg.Height.Should().Be(512);
            cfg.Width.Should().Be(512);
            cfg.RescaleFactor.Should().BeApproximately(1f / 255f, 1e-6f);
            cfg.ImageMean.Should().Equal(0.5f, 0.5f, 0.5f);
        }
        finally { File.Delete(path); }
    }
}
