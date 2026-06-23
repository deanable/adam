using Adam.Shared.Services;
using FluentAssertions;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Tests for <see cref="ConnectionDebugLogger"/>, focusing on the opt-in bounded
/// rotation used by the long-running broker service so its debug log cannot grow
/// without limit within a session.
///
/// The logger is static global state, so these tests share one class (xUnit runs
/// tests within a class sequentially) and isolate the output to a unique temp dir.
/// </summary>
public sealed class ConnectionDebugLoggerTests : IDisposable
{
    private readonly string _dir;

    public ConnectionDebugLoggerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "adam_logtest_" + Guid.NewGuid().ToString("N"));
        ConnectionDebugLogger.LogDirectory = _dir;
    }

    public void Dispose()
    {
        // Disable rotation again so state does not leak to any later consumer,
        // then remove the temp directory.
        ConnectionDebugLogger.EnableRotation(0);
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private int CountEntryLines() =>
        File.ReadAllLines(ConnectionDebugLogger.LogFilePath)
            .Count(l => l.Contains("[INFO ]") || l.Contains("[TRACE]")
                     || l.Contains("[WARN ]") || l.Contains("[ERROR]"));

    [Fact]
    public void Reset_WritesBanner_AndNoEntryLines()
    {
        ConnectionDebugLogger.Reset("banner");

        var text = File.ReadAllText(ConnectionDebugLogger.LogFilePath);
        text.Should().Contain("=== Connection debug log started");
        CountEntryLines().Should().Be(0);
    }

    [Fact]
    public void Rotation_Disabled_AppendsEveryEntry()
    {
        ConnectionDebugLogger.Reset("append");
        ConnectionDebugLogger.EnableRotation(0); // explicit: append-only

        for (var i = 0; i < 250; i++)
            ConnectionDebugLogger.Info($"line {i}");

        CountEntryLines().Should().Be(250);
    }

    [Fact]
    public void Rotation_Enabled_CapsEntriesAtRoughlyTwiceTheLimit()
    {
        const int max = 50;
        ConnectionDebugLogger.Reset("rotate");
        ConnectionDebugLogger.EnableRotation(max);

        // Write far more than the cap.
        for (var i = 0; i < 1000; i++)
            ConnectionDebugLogger.Info($"line {i}");

        // Amortized compaction keeps the file between the cap and ~2x the cap.
        var lines = CountEntryLines();
        lines.Should().BeLessThanOrEqualTo(2 * max);
        lines.Should().BeGreaterThanOrEqualTo(max);
    }

    [Fact]
    public void Rotation_Enabled_KeepsMostRecentEntries_AndBanner()
    {
        const int max = 20;
        ConnectionDebugLogger.Reset("recent");
        ConnectionDebugLogger.EnableRotation(max);

        for (var i = 0; i < 500; i++)
            ConnectionDebugLogger.Info($"line {i}");

        var text = File.ReadAllText(ConnectionDebugLogger.LogFilePath);
        var entries = File.ReadAllLines(ConnectionDebugLogger.LogFilePath);

        // The banner survives compaction...
        text.Should().Contain("=== Connection debug log started");
        // ...the newest entry is present (match the whole token to avoid "line 49"
        // matching "line 499")...
        entries.Should().Contain(l => l.EndsWith(" line 499"));
        // ...and an old, long-evicted entry is gone.
        entries.Should().NotContain(l => l.EndsWith(" line 0"));
    }
}
