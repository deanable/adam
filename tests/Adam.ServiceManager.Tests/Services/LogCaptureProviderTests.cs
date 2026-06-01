using System.Collections.ObjectModel;
using Adam.ServiceManager.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace Adam.ServiceManager.Tests.Services;

public sealed class LogCaptureProviderTests : IDisposable
{
    private readonly ObservableCollection<string> _capture;
    private readonly LogCaptureProvider _sut;
    private readonly ILogger _logger;

    public LogCaptureProviderTests()
    {
        _capture = new ObservableCollection<string>();
        _sut = new LogCaptureProvider(_capture);
        _logger = _sut.CreateLogger("Adam.ServiceManager.Services.TestService");
    }

    [Fact]
    public void CreateLogger_ReturnsLoggerThatCapturesMessages()
    {
        // Act
        _logger.LogInformation("Hello, world!");

        // Assert
        _capture.Should().ContainSingle(e => e.Contains("Hello, world!"));
    }

    [Fact]
    public void Log_FormatsEntryWithTimestampPrefix()
    {
        // Act
        _logger.LogInformation("Test message");

        // Assert
        _capture.Should().ContainSingle(e => e.StartsWith("["));
    }

    [Fact]
    public void Log_IncludesLogLevelAbbreviation()
    {
        // Act
        _logger.LogTrace("trace");
        _logger.LogDebug("debug");
        _logger.LogInformation("info");
        _logger.LogWarning("warn");
        _logger.LogError("error");
        _logger.LogCritical("critical");

        // Assert
        _capture.Should().Contain(e => e.Contains("|TRCE|"));
        _capture.Should().Contain(e => e.Contains("|DBUG|"));
        _capture.Should().Contain(e => e.Contains("|INFO|"));
        _capture.Should().Contain(e => e.Contains("|WARN|"));
        _capture.Should().Contain(e => e.Contains("|FAIL|"));
        _capture.Should().Contain(e => e.Contains("|CRIT|"));
    }

    [Fact]
    public void Log_ShortensCategoryToClassName()
    {
        // Act
        _logger.LogInformation("test");

        // Assert
        _capture.Should().ContainSingle(e => e.Contains("|TestService]"));
    }

    [Fact]
    public void Log_HandlesCategoryWithoutDot()
    {
        // Arrange
        var logger = _sut.CreateLogger("NoDot");

        // Act
        logger.LogInformation("test");

        // Assert
        _capture.Should().ContainSingle(e => e.Contains("|NoDot]"));
    }

    [Fact]
    public void Log_IncludesFormattedMessage()
    {
        // Act
        _logger.LogInformation("User {UserId} logged in at {Time}", 42, "12:00");

        // Assert
        _capture.Should().ContainSingle(e => e.Contains("User 42 logged in at 12:00"));
    }

    [Fact]
    public void Log_IncludesExceptionDetails()
    {
        // Arrange
        var ex = new InvalidOperationException("Something went wrong");

        // Act
        _logger.LogError(ex, "Operation failed");

        // Assert
        _capture.Should().ContainSingle(e => e.Contains("Operation failed"));
    }

    [Fact]
    public void Log_TruncatesAtMaxEntries()
    {
        // Act: add 2010 entries (max is 2000)
        for (int i = 0; i < 2010; i++)
        {
            _logger.LogInformation("Entry {Number}", i);
        }

        // Assert: should be at most 2000 entries
        _capture.Count.Should().Be(2000);
        // The oldest entries should be removed
        _capture.Should().NotContain(e => e.Contains("Entry 0"));
        // The newest entries should be present
        _capture.Should().Contain(e => e.Contains("Entry 2009"));
    }

    [Fact]
    public void IsEnabled_ReturnsTrueForAllLevels()
    {
        // Assert
        _logger.IsEnabled(LogLevel.Trace).Should().BeTrue();
        _logger.IsEnabled(LogLevel.Critical).Should().BeTrue();
    }

    [Fact]
    public void BeginScope_ReturnsNull()
    {
        // Act
        var scope = _logger.BeginScope("scope");

        // Assert
        scope.Should().BeNull();
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        // Act
        var act = () => _sut.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void MultipleLoggers_ShareSameCaptureCollection()
    {
        // Arrange
        var logger1 = _sut.CreateLogger("Service1");
        var logger2 = _sut.CreateLogger("Service2");

        // Act
        logger1.LogInformation("From one");
        logger2.LogInformation("From two");

        // Assert
        _capture.Should().Contain(e => e.Contains("From one"));
        _capture.Should().Contain(e => e.Contains("From two"));
    }

    public void Dispose()
    {
        _sut.Dispose();
    }
}
