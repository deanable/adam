using Adam.BrokerService.Handlers;
using Adam.Shared.Contracts;
using Adam.Shared.Services;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.BrokerService.Tests;

/// <summary>
/// Unit tests for <see cref="StatusHandler.StartServiceAsync"/> and <see cref="StatusHandler.StopServiceAsync"/>.
/// Uses a <see cref="MockServiceInstaller"/> to control installer behavior and verify calls.
/// </summary>
public sealed class StatusHandlerTests
{
    private readonly string _correlationId = Guid.NewGuid().ToString();

    private static Envelope CreateStartRequest(string correlationId) => new()
    {
        CorrelationId = correlationId,
        MessageType = MessageTypeCode.StartServiceRequest,
        Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new StartServiceRequest()))
    };

    private static Envelope CreateStopRequest(string correlationId) => new()
    {
        CorrelationId = correlationId,
        MessageType = MessageTypeCode.StopServiceRequest,
        Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new StopServiceRequest()))
    };

    // =========================================================================
    // StartServiceAsync – no installer available
    // =========================================================================

    [Fact]
    public async Task StartServiceAsync_NoInstaller_ReturnsError()
    {
        var handler = CreateHandler(installer: null);

        var response = await handler.StartServiceAsync(CreateStartRequest(_correlationId), default);

        response.StatusCode.Should().Be(13);
        response.MessageType.Should().Be(MessageTypeCode.StartServiceResponse);
        response.CorrelationId.Should().Be(_correlationId);
        var payload = ProtoHelper.Deserialize<StopServiceResponse>(response.Payload.ToByteArray());
        payload.Success.Should().BeFalse();
        payload.Message.Should().Contain("No service installer available");
    }

    // =========================================================================
    // StartServiceAsync – service not installed
    // =========================================================================

    [Fact]
    public async Task StartServiceAsync_ServiceNotInstalled_ReturnsError()
    {
        var mockInstaller = new MockServiceInstaller
        {
            IsSupportedValue = true,
            GetStatusResult = ServiceStatus.NotInstalled
        };
        var handler = CreateHandler(mockInstaller);

        var response = await handler.StartServiceAsync(CreateStartRequest(_correlationId), default);

        response.StatusCode.Should().Be(5);
        response.MessageType.Should().Be(MessageTypeCode.StartServiceResponse);
        response.CorrelationId.Should().Be(_correlationId);
        var payload = ProtoHelper.Deserialize<StopServiceResponse>(response.Payload.ToByteArray());
        payload.Success.Should().BeFalse();
        payload.Message.Should().Contain("not installed");
    }

    // =========================================================================
    // StartServiceAsync – already running
    // =========================================================================

    [Fact]
    public async Task StartServiceAsync_AlreadyRunning_ReturnsSuccess()
    {
        var mockInstaller = new MockServiceInstaller
        {
            IsSupportedValue = true,
            GetStatusResult = ServiceStatus.Running
        };
        var handler = CreateHandler(mockInstaller);

        var response = await handler.StartServiceAsync(CreateStartRequest(_correlationId), default);

        response.StatusCode.Should().Be(0);
        response.MessageType.Should().Be(MessageTypeCode.StartServiceResponse);
        response.CorrelationId.Should().Be(_correlationId);
        var payload = ProtoHelper.Deserialize<StopServiceResponse>(response.Payload.ToByteArray());
        payload.Success.Should().BeTrue();
        payload.Message.Should().Contain("already running");
        mockInstaller.StartCallCount.Should().Be(0);
    }

    // =========================================================================
    // StartServiceAsync – starts successfully
    // =========================================================================

    [Fact]
    public async Task StartServiceAsync_StartsSuccessfully()
    {
        var mockInstaller = new MockServiceInstaller
        {
            IsSupportedValue = true,
            GetStatusResult = ServiceStatus.Stopped
        };
        var handler = CreateHandler(mockInstaller);

        var response = await handler.StartServiceAsync(CreateStartRequest(_correlationId), default);

        response.StatusCode.Should().Be(0);
        response.MessageType.Should().Be(MessageTypeCode.StartServiceResponse);
        response.CorrelationId.Should().Be(_correlationId);
        var payload = ProtoHelper.Deserialize<StopServiceResponse>(response.Payload.ToByteArray());
        payload.Success.Should().BeTrue();
        payload.Message.Should().Contain("started successfully");
        mockInstaller.StartCallCount.Should().Be(1);
        mockInstaller.StopCallCount.Should().Be(0);
    }

    // =========================================================================
    // StartServiceAsync – installer throws
    // =========================================================================

    [Fact]
    public async Task StartServiceAsync_InstallerThrows_ReturnsError()
    {
        var mockInstaller = new MockServiceInstaller
        {
            IsSupportedValue = true,
            GetStatusResult = ServiceStatus.Stopped,
            StartThrows = new InvalidOperationException("Access denied")
        };
        var handler = CreateHandler(mockInstaller);

        var response = await handler.StartServiceAsync(CreateStartRequest(_correlationId), default);

        response.StatusCode.Should().Be(13);
        response.MessageType.Should().Be(MessageTypeCode.StartServiceResponse);
        response.CorrelationId.Should().Be(_correlationId);
        var payload = ProtoHelper.Deserialize<StopServiceResponse>(response.Payload.ToByteArray());
        payload.Success.Should().BeFalse();
        payload.Message.Should().Contain("Access denied");
        mockInstaller.StartCallCount.Should().Be(1);
    }

    // =========================================================================
    // StopServiceAsync – no installer available
    // =========================================================================

    [Fact]
    public async Task StopServiceAsync_NoInstaller_ReturnsError()
    {
        var handler = CreateHandler(installer: null);

        var response = await handler.StopServiceAsync(CreateStopRequest(_correlationId), default);

        response.StatusCode.Should().Be(13);
        response.MessageType.Should().Be(MessageTypeCode.StopServiceResponse);
        response.CorrelationId.Should().Be(_correlationId);
        var payload = ProtoHelper.Deserialize<StopServiceResponse>(response.Payload.ToByteArray());
        payload.Success.Should().BeFalse();
        payload.Message.Should().Contain("No service installer available");
    }

    // =========================================================================
    // StopServiceAsync – service not installed
    // =========================================================================

    [Fact]
    public async Task StopServiceAsync_ServiceNotInstalled_ReturnsError()
    {
        var mockInstaller = new MockServiceInstaller
        {
            IsSupportedValue = true,
            GetStatusResult = ServiceStatus.NotInstalled
        };
        var handler = CreateHandler(mockInstaller);

        var response = await handler.StopServiceAsync(CreateStopRequest(_correlationId), default);

        response.StatusCode.Should().Be(5);
        response.MessageType.Should().Be(MessageTypeCode.StopServiceResponse);
        response.CorrelationId.Should().Be(_correlationId);
        var payload = ProtoHelper.Deserialize<StopServiceResponse>(response.Payload.ToByteArray());
        payload.Success.Should().BeFalse();
        payload.Message.Should().Contain("not installed");
    }

    // =========================================================================
    // StopServiceAsync – already stopped
    // =========================================================================

    [Fact]
    public async Task StopServiceAsync_AlreadyStopped_ReturnsSuccess()
    {
        var mockInstaller = new MockServiceInstaller
        {
            IsSupportedValue = true,
            GetStatusResult = ServiceStatus.Stopped
        };
        var handler = CreateHandler(mockInstaller);

        var response = await handler.StopServiceAsync(CreateStopRequest(_correlationId), default);

        response.StatusCode.Should().Be(0);
        response.MessageType.Should().Be(MessageTypeCode.StopServiceResponse);
        response.CorrelationId.Should().Be(_correlationId);
        var payload = ProtoHelper.Deserialize<StopServiceResponse>(response.Payload.ToByteArray());
        payload.Success.Should().BeTrue();
        payload.Message.Should().Contain("already stopped");
        mockInstaller.StopCallCount.Should().Be(0);
    }

    // =========================================================================
    // StopServiceAsync – stops successfully
    // =========================================================================

    [Fact]
    public async Task StopServiceAsync_StopsSuccessfully()
    {
        var mockInstaller = new MockServiceInstaller
        {
            IsSupportedValue = true,
            GetStatusResult = ServiceStatus.Running
        };
        var handler = CreateHandler(mockInstaller);

        var response = await handler.StopServiceAsync(CreateStopRequest(_correlationId), default);

        response.StatusCode.Should().Be(0);
        response.MessageType.Should().Be(MessageTypeCode.StopServiceResponse);
        response.CorrelationId.Should().Be(_correlationId);
        var payload = ProtoHelper.Deserialize<StopServiceResponse>(response.Payload.ToByteArray());
        payload.Success.Should().BeTrue();
        payload.Message.Should().Contain("stopped successfully");
        mockInstaller.StopCallCount.Should().Be(1);
        mockInstaller.StartCallCount.Should().Be(0);
    }

    // =========================================================================
    // StopServiceAsync – installer throws
    // =========================================================================

    [Fact]
    public async Task StopServiceAsync_InstallerThrows_ReturnsError()
    {
        var mockInstaller = new MockServiceInstaller
        {
            IsSupportedValue = true,
            GetStatusResult = ServiceStatus.Running,
            StopThrows = new InvalidOperationException("Service won't stop")
        };
        var handler = CreateHandler(mockInstaller);

        var response = await handler.StopServiceAsync(CreateStopRequest(_correlationId), default);

        response.StatusCode.Should().Be(13);
        response.MessageType.Should().Be(MessageTypeCode.StopServiceResponse);
        response.CorrelationId.Should().Be(_correlationId);
        var payload = ProtoHelper.Deserialize<StopServiceResponse>(response.Payload.ToByteArray());
        payload.Success.Should().BeFalse();
        payload.Message.Should().Contain("Service won't stop");
        mockInstaller.StopCallCount.Should().Be(1);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static StatusHandler CreateHandler(IServiceInstaller? installer)
    {
        var services = new ServiceCollection();

        // Register logging so StatusHandler can resolve ILogger<StatusHandler>
        services.AddLogging();

        services.AddSingleton<StatusHandler>();

        if (installer != null)
            services.AddSingleton<IServiceInstaller>(installer);

        // Also register a non-supported installer to verify the "first supported" resolution
        services.AddSingleton<IServiceInstaller>(new MockServiceInstaller
        {
            IsSupportedValue = false,
            ServiceNameValue = "UnsupportedInstaller"
        });

        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<StatusHandler>();
    }

    /// <summary>
    /// Manual mock of <see cref="IServiceInstaller"/> with configurable return values
    /// and call-count tracking for StartAsync/StopAsync.
    /// </summary>
    private sealed class MockServiceInstaller : IServiceInstaller
    {
        public string ServiceNameValue { get; set; } = "MockInstaller";
        public bool IsSupportedValue { get; set; }

        public ServiceStatus GetStatusResult { get; set; } = ServiceStatus.Stopped;
        public Exception? StartThrows { get; set; }
        public Exception? StopThrows { get; set; }

        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }

        public string ServiceName => ServiceNameValue;
        public bool IsSupported => IsSupportedValue;

        public Task InstallAsync(string brokerPath, int port, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UninstallAsync(CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(GetStatusResult);

        public Task StartAsync(CancellationToken ct = default)
        {
            StartCallCount++;
            if (StartThrows != null)
                throw StartThrows;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            StopCallCount++;
            if (StopThrows != null)
                throw StopThrows;
            return Task.CompletedTask;
        }
    }
}
