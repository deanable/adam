using System.Text.Json;
using Adam.ServiceManager.Services;
using FluentAssertions;

namespace Adam.ServiceManager.Tests.Services;

public sealed class ElevatedHelperTests
{
    [Fact]
    public async Task RunAsync_WhenRequestFileNotFound_ReturnsErrorResult()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act
        var exitCode = await ElevatedHelper.RunAsync(tempFile);

        // Assert
        exitCode.Should().Be(1);
        // The helper writes error response back to the file
        File.Exists(tempFile).Should().BeTrue();
        var response = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(tempFile));
        response.GetProperty("Success").GetBoolean().Should().BeFalse();

        // Cleanup
        File.Delete(tempFile);
    }

    [Fact]
    public async Task RunAsync_WhenRequestHasUnknownOperation_ReturnsError()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var request = new { Operation = "reboot", BrokerPath = "test.exe", Port = 9100 };
        await File.WriteAllTextAsync(tempFile, JsonSerializer.Serialize(request));

        // Act
        var exitCode = await ElevatedHelper.RunAsync(tempFile);

        // Assert
        exitCode.Should().Be(1);
        var response = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(tempFile));
        response.GetProperty("Success").GetBoolean().Should().BeFalse();
        response.GetProperty("ErrorMessage").GetString().Should().Contain("Unknown elevated operation");

        // Cleanup
        File.Delete(tempFile);
    }

    [Fact]
    public async Task RunAsync_WhenInstallMissingBrokerPath_ReturnsError()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var request = new { Operation = "install", BrokerPath = "", Port = 9100 };
        await File.WriteAllTextAsync(tempFile, JsonSerializer.Serialize(request));

        // Act
        var exitCode = await ElevatedHelper.RunAsync(tempFile);

        // Assert
        exitCode.Should().Be(1);
        var response = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(tempFile));
        response.GetProperty("Success").GetBoolean().Should().BeFalse();
        response.GetProperty("ErrorMessage").GetString().Should().Contain("BrokerPath");

        // Cleanup
        File.Delete(tempFile);
    }

    [Fact]
    public async Task RunAsync_WhenRequestIsInvalidJson_WritesErrorResponse()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        await File.WriteAllTextAsync(tempFile, "not valid json at all {{{");

        // Act
        var exitCode = await ElevatedHelper.RunAsync(tempFile);

        // Assert
        exitCode.Should().Be(1);
        var response = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(tempFile));
        response.GetProperty("Success").GetBoolean().Should().BeFalse();

        // Cleanup
        File.Delete(tempFile);
    }

    [Fact]
    public async Task RunAsync_WhenRequestIsNullOperation_WritesErrorResponse()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var request = new { Operation = "", BrokerPath = "test.exe", Port = 9100 };
        await File.WriteAllTextAsync(tempFile, JsonSerializer.Serialize(request));

        // Act
        var exitCode = await ElevatedHelper.RunAsync(tempFile);

        // Assert
        exitCode.Should().Be(1);
        var response = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(tempFile));
        response.GetProperty("Success").GetBoolean().Should().BeFalse();
        response.GetProperty("ErrorMessage").GetString().Should().Contain("Invalid elevated request");

        // Cleanup
        File.Delete(tempFile);
    }

    [Fact]
    public async Task RunAsync_WhenStartOperation_ThrowsWindowsServiceInstallerException()
    {
        // On non-Windows (or no BrokerService installed), start will throw.
        // We just verify the helper returns exit code 1 and writes an error response.
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var request = new { Operation = "start", BrokerPath = "test.exe", Port = 9100 };
        await File.WriteAllTextAsync(tempFile, JsonSerializer.Serialize(request));

        // Act
        var exitCode = await ElevatedHelper.RunAsync(tempFile);

        // Assert
        exitCode.Should().Be(1);
        var response = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(tempFile));
        response.GetProperty("Success").GetBoolean().Should().BeFalse();

        // Cleanup
        File.Delete(tempFile);
    }

    [Fact]
    public async Task RunAsync_WhenStopOperation_ThrowsWindowsServiceInstallerException()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var request = new { Operation = "stop", BrokerPath = "test.exe", Port = 9100 };
        await File.WriteAllTextAsync(tempFile, JsonSerializer.Serialize(request));

        // Act
        var exitCode = await ElevatedHelper.RunAsync(tempFile);

        // Assert
        exitCode.Should().Be(1);
        var response = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(tempFile));
        response.GetProperty("Success").GetBoolean().Should().BeFalse();

        // Cleanup
        File.Delete(tempFile);
    }
}
