using System.Security.Principal;
using System.Text.Json;
using Adam.Shared.Services;
using FluentAssertions;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Tests for the self-elevation flow in <see cref="WindowsServiceInstaller"/>.
///
/// Covers two areas:
/// 1. Direct <see cref="ElevatedRequest"/> / <see cref="ElevatedResponse"/> JSON serialization
/// 2. Mocked process elevation flow that intercepts the process launch via
///    <see cref="WindowsServiceInstaller.ElevatedProcessHandler"/> and verifies the
///    serialized request for each operation type (install, uninstall, start, stop).
/// </summary>
public sealed class ElevatedFlowTests
{
    // ══════════════════════════════════════════════════════════════
    //  ElevatedRequest serialization
    // ══════════════════════════════════════════════════════════════

    public sealed class ElevatedRequestSerialization
    {
        [Fact]
        public void Install_IncludesOperationBrokerPathAndPort()
        {
            var request = new ElevatedRequest
            {
                Operation = "install",
                BrokerPath = @"C:\Services\Adam.BrokerService.exe",
                Port = 9100
            };

            var json = JsonSerializer.Serialize(request);
            var deserialized = JsonSerializer.Deserialize<ElevatedRequest>(json);

            deserialized.Should().NotBeNull();
            deserialized!.Operation.Should().Be("install");
            deserialized.BrokerPath.Should().Be(@"C:\Services\Adam.BrokerService.exe");
            deserialized.Port.Should().Be(9100);
        }

        [Fact]
        public void Uninstall_IncludesOperationOnly()
        {
            var request = new ElevatedRequest { Operation = "uninstall" };

            var json = JsonSerializer.Serialize(request);
            var deserialized = JsonSerializer.Deserialize<ElevatedRequest>(json);

            deserialized.Should().NotBeNull();
            deserialized!.Operation.Should().Be("uninstall");
            deserialized.BrokerPath.Should().BeNull();
            deserialized.Port.Should().Be(0);
        }

        [Fact]
        public void Start_IncludesOperationOnly()
        {
            var request = new ElevatedRequest { Operation = "start" };

            var json = JsonSerializer.Serialize(request);
            var deserialized = JsonSerializer.Deserialize<ElevatedRequest>(json);

            deserialized.Should().NotBeNull();
            deserialized!.Operation.Should().Be("start");
            deserialized.BrokerPath.Should().BeNull();
            deserialized.Port.Should().Be(0);
        }

        [Fact]
        public void Stop_IncludesOperationOnly()
        {
            var request = new ElevatedRequest { Operation = "stop" };

            var json = JsonSerializer.Serialize(request);
            var deserialized = JsonSerializer.Deserialize<ElevatedRequest>(json);

            deserialized.Should().NotBeNull();
            deserialized!.Operation.Should().Be("stop");
            deserialized.BrokerPath.Should().BeNull();
            deserialized.Port.Should().Be(0);
        }

        [Fact]
        public void Default_HasEmptyOperation()
        {
            var request = new ElevatedRequest();

            request.Operation.Should().BeEmpty();
            request.BrokerPath.Should().BeNull();
            request.Port.Should().Be(0);
        }

        [Fact]
        public void RoundTrip_PreservesAllProperties()
        {
            var original = new ElevatedRequest
            {
                Operation = "install",
                BrokerPath = @"/opt/adam/Adam.BrokerService",
                Port = 8080
            };

            var json = JsonSerializer.Serialize(original);
            var restored = JsonSerializer.Deserialize<ElevatedRequest>(json);

            restored.Should().NotBeNull();
            restored.Should().BeEquivalentTo(original);
        }

        [Fact]
        public void JsonPropertyNames_ArePascalCaseByDefault()
        {
            var request = new ElevatedRequest
            {
                Operation = "test",
                BrokerPath = "/path",
                Port = 1234
            };

            var json = JsonSerializer.Serialize(request);

            // System.Text.Json uses exact property names (PascalCase for C# records) by default
            json.Should().Contain("\"Operation\"");
            json.Should().Contain("\"BrokerPath\"");
            json.Should().Contain("\"Port\"");
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  ElevatedResponse serialization
    // ══════════════════════════════════════════════════════════════

    public sealed class ElevatedResponseSerialization
    {
        [Fact]
        public void Success_SerializesAndDeserializes()
        {
            var response = new ElevatedResponse { Success = true };

            var json = JsonSerializer.Serialize(response);
            var deserialized = JsonSerializer.Deserialize<ElevatedResponse>(json);

            deserialized.Should().NotBeNull();
            deserialized!.Success.Should().BeTrue();
            deserialized.ErrorMessage.Should().BeNull();
        }

        [Fact]
        public void Failure_WithMessage_SerializesAndDeserializes()
        {
            var response = new ElevatedResponse
            {
                Success = false,
                ErrorMessage = "Access denied."
            };

            var json = JsonSerializer.Serialize(response);
            var deserialized = JsonSerializer.Deserialize<ElevatedResponse>(json);

            deserialized.Should().NotBeNull();
            deserialized!.Success.Should().BeFalse();
            deserialized.ErrorMessage.Should().Be("Access denied.");
        }

        [Fact]
        public void Default_SuccessIsFalse()
        {
            var response = new ElevatedResponse();

            response.Success.Should().BeFalse();
            response.ErrorMessage.Should().BeNull();
        }

        [Fact]
        public void RoundTrip_PreservesAllProperties()
        {
            var original = new ElevatedResponse
            {
                Success = true,
                ErrorMessage = null
            };

            var json = JsonSerializer.Serialize(original);
            var restored = JsonSerializer.Deserialize<ElevatedResponse>(json);

            restored.Should().NotBeNull();
            restored.Should().BeEquivalentTo(original);
        }

        [Fact]
        public void JsonPropertyNames_ArePascalCaseByDefault()
        {
            var response = new ElevatedResponse { Success = true };

            var json = JsonSerializer.Serialize(response);

            // System.Text.Json uses exact property names (PascalCase for C# records) by default
            json.Should().Contain("\"Success\"");
            json.Should().Contain("\"ErrorMessage\"");
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Self-elevation flow with mocked process launch
    // ══════════════════════════════════════════════════════════════
    //
    //  These tests use the ElevatedProcessHandler hook to intercept
    //  the process launch, capturing and verifying the serialized
    //  ElevatedRequest for each operation type.
    //
    //  They require:
    //    - Running on Windows (so IsSupported is true)
    //    - NOT running as Administrator (so the !IsElevated branch is hit)
    //
    //  On non-Windows or elevated sessions, the tests silently pass
    //  (the if-guard at the top returns early).

    public sealed class MockElevatedProcessFlow
    {
        private const string TestBrokerPath = @"C:\Test\Adam.BrokerService.exe";
        private const int TestPort = 9100;

        /// <summary>
        /// Returns true only on Windows when the process is NOT running as Administrator.
        /// In this environment the elevation code path is exercised.
        /// </summary>
        private static bool CanTestElevationMock()
        {
            if (!OperatingSystem.IsWindows())
                return false;
#pragma warning disable CA1416 // Validate platform compatibility
            return !new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);
#pragma warning restore CA1416
        }

        [Fact]
        public async Task InstallAsync_SendsElevatedRequestWithBrokerPathAndPort()
        {
            if (!CanTestElevationMock()) return;

            ElevatedRequest? captured = null;

            var installer = new WindowsServiceInstaller
            {
                ElevatedProcessHandler = async (requestFile, ct) =>
                {
                    var json = await File.ReadAllTextAsync(requestFile, ct);
                    captured = JsonSerializer.Deserialize<ElevatedRequest>(json);

                    var response = JsonSerializer.Serialize(new ElevatedResponse { Success = true });
                    await File.WriteAllTextAsync(requestFile, response, ct);
                }
            };

            await installer.InstallAsync(TestBrokerPath, TestPort);

            captured.Should().NotBeNull();
            captured!.Operation.Should().Be("install");
            captured.BrokerPath.Should().Be(TestBrokerPath);
            captured.Port.Should().Be(TestPort);
        }

        [Fact]
        public async Task UninstallAsync_SendsElevatedRequestWithUninstallOperation()
        {
            if (!CanTestElevationMock()) return;

            ElevatedRequest? captured = null;

            var installer = new WindowsServiceInstaller
            {
                ElevatedProcessHandler = async (requestFile, ct) =>
                {
                    var json = await File.ReadAllTextAsync(requestFile, ct);
                    captured = JsonSerializer.Deserialize<ElevatedRequest>(json);

                    var response = JsonSerializer.Serialize(new ElevatedResponse { Success = true });
                    await File.WriteAllTextAsync(requestFile, response, ct);
                }
            };

            await installer.UninstallAsync();

            captured.Should().NotBeNull();
            captured!.Operation.Should().Be("uninstall");
            captured.BrokerPath.Should().BeNull();
            captured.Port.Should().Be(0);
        }

        [Fact]
        public async Task StartAsync_SendsElevatedRequestWithStartOperation()
        {
            if (!CanTestElevationMock()) return;

            ElevatedRequest? captured = null;

            var installer = new WindowsServiceInstaller
            {
                ElevatedProcessHandler = async (requestFile, ct) =>
                {
                    var json = await File.ReadAllTextAsync(requestFile, ct);
                    captured = JsonSerializer.Deserialize<ElevatedRequest>(json);

                    var response = JsonSerializer.Serialize(new ElevatedResponse { Success = true });
                    await File.WriteAllTextAsync(requestFile, response, ct);
                }
            };

            await installer.StartAsync();

            captured.Should().NotBeNull();
            captured!.Operation.Should().Be("start");
        }

        [Fact]
        public async Task StopAsync_SendsElevatedRequestWithStopOperation()
        {
            if (!CanTestElevationMock()) return;

            ElevatedRequest? captured = null;

            var installer = new WindowsServiceInstaller
            {
                ElevatedProcessHandler = async (requestFile, ct) =>
                {
                    var json = await File.ReadAllTextAsync(requestFile, ct);
                    captured = JsonSerializer.Deserialize<ElevatedRequest>(json);

                    var response = JsonSerializer.Serialize(new ElevatedResponse { Success = true });
                    await File.WriteAllTextAsync(requestFile, response, ct);
                }
            };

            await installer.StopAsync();

            captured.Should().NotBeNull();
            captured!.Operation.Should().Be("stop");
        }

        [Fact]
        public async Task InstallAsync_ErrorResponse_ThrowsInvalidOperationException()
        {
            if (!CanTestElevationMock()) return;

            var installer = new WindowsServiceInstaller
            {
                ElevatedProcessHandler = async (requestFile, ct) =>
                {
                    var response = JsonSerializer.Serialize(new ElevatedResponse
                    {
                        Success = false,
                        ErrorMessage = "Simulated elevated failure"
                    });
                    await File.WriteAllTextAsync(requestFile, response, ct);
                }
            };

            var act = async () => await installer.InstallAsync(TestBrokerPath, TestPort);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Simulated elevated failure");
        }

        [Fact]
        public async Task InstallAsync_HandlerDoesNotWriteResponse_ThrowsForDefaultFailureResponse()
        {
            if (!CanTestElevationMock()) return;

            var installer = new WindowsServiceInstaller
            {
                ElevatedProcessHandler = async (_, _) =>
                {
                    // Intentionally do nothing — the temp file still contains the
                    // request JSON (Valid JSON but wrong shape for ElevatedResponse).
                    // Deserializing as ElevatedResponse yields Success=false, ErrorMessage=null.
                    await Task.CompletedTask;
                }
            };

            var act = async () => await installer.InstallAsync(TestBrokerPath, TestPort);

            // The request JSON properties (Operation, BrokerPath, Port) don't map to
            // ElevatedResponse's properties (Success, ErrorMessage), so deserialization
            // succeeds with defaults: Success=false, ErrorMessage=null.
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Elevated operation failed with no error message.");
        }

        [Fact]
        public async Task InstallAsync_LogsElevatedProcessHandlerUsage()
        {
            if (!CanTestElevationMock()) return;

            var logger = new TestLogger<WindowsServiceInstaller>();
            var installer = new WindowsServiceInstaller(logger)
            {
                ElevatedProcessHandler = async (requestFile, ct) =>
                {
                    var response = JsonSerializer.Serialize(new ElevatedResponse { Success = true });
                    await File.WriteAllTextAsync(requestFile, response, ct);
                }
            };

            await installer.InstallAsync(TestBrokerPath, TestPort);

            logger.Messages.Should().Contain(m =>
                m.Contains("ElevatedProcessHandler is set"));
            logger.Messages.Should().Contain(m =>
                m.Contains("Elevated operation completed successfully"));
        }
    }
}
