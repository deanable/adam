using FluentAssertions;
using LiquidVision.Core.Services;
using Float16 = Microsoft.ML.OnnxRuntime.Float16;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Pure (no-ONNX) tests for the precision-independent token-selection helpers extracted from
/// <see cref="Lfm2VlGenerator"/>, plus a guard on the fp32&lt;-&gt;fp16 conversion path used at the
/// ONNX boundary for q4f16 graphs.
/// </summary>
public sealed class Lfm2VlGeneratorSamplingTests
{
    [Fact]
    public void Argmax_returns_index_of_max_logit()
    {
        var logits = new[] { -1.0f, 0.5f, 3.2f, 3.1f, -5f };
        Lfm2VlGenerator.Argmax(logits, logits.Length).Should().Be(2);
    }

    [Fact]
    public void Argmax_handles_negative_only_logits()
    {
        var logits = new[] { -10f, -3f, -7f, -2.5f };
        Lfm2VlGenerator.Argmax(logits, logits.Length).Should().Be(3);
    }

    [Fact]
    public void SampleTopP_is_deterministic_for_a_fixed_seed()
    {
        var logits = new[] { 1.0f, 2.0f, 0.5f, 3.0f, 1.5f };

        int a = Lfm2VlGenerator.SampleTopP(logits, logits.Length, temperature: 0.7f, topP: 0.9f, seed: 42);
        int b = Lfm2VlGenerator.SampleTopP(logits, logits.Length, temperature: 0.7f, topP: 0.9f, seed: 42);

        b.Should().Be(a);
    }

    [Fact]
    public void SampleTopP_with_tiny_topP_selects_the_argmax()
    {
        // A nucleus that admits only the top probability mass must return the highest-logit token.
        var logits = new[] { 0.1f, 0.2f, 5.0f, 0.3f };
        int picked = Lfm2VlGenerator.SampleTopP(logits, logits.Length, temperature: 1.0f, topP: 1e-6f, seed: 1);
        picked.Should().Be(2);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(-1f)]
    [InlineData(0.5f)]
    [InlineData(-123.25f)]
    public void Float16_roundtrip_is_value_correct(float value)
    {
        // Guards against the new Float16(ushort) raw-bits trap: the explicit operators must round-trip by value.
        float back = (float)(Float16)value;
        back.Should().BeApproximately(value, 0.05f);
    }

    [Fact]
    public void Float16_default_is_positive_zero()
    {
        // Cache zero-init relies on default(Float16) == +0.0.
        ((float)default(Float16)).Should().Be(0f);
    }
}
