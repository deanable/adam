using System.Diagnostics;
using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
using Adam.Shared.Services;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

public sealed class StatusHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StatusHandler> _logger;
    private readonly DateTime _startTime = DateTime.UtcNow;

    public StatusHandler(IServiceProvider serviceProvider, ILogger<StatusHandler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task<Envelope> GetStatusAsync(Envelope request, CancellationToken ct)
    {
        var listener = _serviceProvider.GetRequiredService<TcpListenerService>();

        var response = new GetServiceStatusResponse
        {
            ActiveConnections = listener.ActiveConnectionCount,
            RejectedConnections = listener.RejectedConnectionCount,
            Port = listener.Port,
            UptimeSeconds = (long)(DateTime.UtcNow - _startTime).TotalSeconds,
            ServiceState = "Running"
        };

        return Task.FromResult(new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.GetServiceStatusResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        });
    }

    public async Task<Envelope> StartServiceAsync(Envelope request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[TIMING] StatusHandler.StartServiceAsync() — remote start request received (CorrelationId={CorrelationId})",
            request.CorrelationId);

        try
        {
            var installer = ResolveInstaller();
            if (installer == null)
            {
                _logger.LogWarning("No service installer available for this platform");
                return ErrorResponse(request, ErrorCode.InternalError, "No service installer available for this platform");
            }

            _logger.LogInformation("[DIAG] Resolved installer: {Type} (IsSupported={IsSupported}, ServiceName='{ServiceName}')",
                installer.GetType().Name, installer.IsSupported, installer.ServiceName);

            _logger.LogInformation("[TIMING] Querying service status (elapsed: {ElapsedMs:F0}ms)...", sw.Elapsed.TotalMilliseconds);
            var status = await installer.GetStatusAsync(ct);
            _logger.LogInformation("[TIMING] Service status = {Status} (elapsed: {ElapsedMs:F0}ms)", status, sw.Elapsed.TotalMilliseconds);

            if (status == ServiceStatus.NotInstalled)
            {
                _logger.LogWarning("Service is not installed — returning error");
                return ErrorResponse(request, ErrorCode.NotFound, "Service is not installed");
            }

            if (status == ServiceStatus.Running)
            {
                _logger.LogInformation("Service is already running — returning success");
                return SuccessResponse(request, "Service is already running");
            }

            if (status == ServiceStatus.Unknown)
            {
                // Service is in a transitional state (e.g. START_PENDING from a hung start).
                // The StartAsync call handles this internally by stopping first then starting.
                _logger.LogWarning("Service in transitional/unknown state ({Status}) — StartAsync will attempt stop-then-start recovery", status);
            }

            _logger.LogInformation("[TIMING] Calling installer.StartAsync() (elapsed: {ElapsedMs:F0}ms)...", sw.Elapsed.TotalMilliseconds);
            await installer.StartAsync(ct);
            _logger.LogInformation("[TIMING] installer.StartAsync() completed in {ElapsedMs:F0}ms total", sw.Elapsed.TotalMilliseconds);
            _logger.LogInformation("Service started successfully via remote request");

            return SuccessResponse(request, "Service started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TIMING] Failed to start service via remote request after {ElapsedMs:F0}ms", sw.Elapsed.TotalMilliseconds);
            return ErrorResponse(request, ErrorCode.InternalError, $"Failed to start service: {ex.Message}");
        }
    }

    public async Task<Envelope> StopServiceAsync(Envelope request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[TIMING] StatusHandler.StopServiceAsync() — remote stop request received (CorrelationId={CorrelationId})",
            request.CorrelationId);

        try
        {
            var installer = ResolveInstaller();
            if (installer == null)
            {
                _logger.LogWarning("No service installer available for this platform");
                return ErrorResponse(request, ErrorCode.InternalError, "No service installer available for this platform");
            }

            _logger.LogInformation("[DIAG] Resolved installer: {Type} (IsSupported={IsSupported}, ServiceName='{ServiceName}')",
                installer.GetType().Name, installer.IsSupported, installer.ServiceName);

            _logger.LogInformation("[TIMING] Querying service status (elapsed: {ElapsedMs:F0}ms)...", sw.Elapsed.TotalMilliseconds);
            var status = await installer.GetStatusAsync(ct);
            _logger.LogInformation("[TIMING] Service status = {Status} (elapsed: {ElapsedMs:F0}ms)", status, sw.Elapsed.TotalMilliseconds);

            if (status == ServiceStatus.NotInstalled)
            {
                _logger.LogWarning("Service is not installed — returning error");
                return ErrorResponse(request, ErrorCode.NotFound, "Service is not installed");
            }

            if (status == ServiceStatus.Stopped)
            {
                _logger.LogInformation("Service is already stopped — returning success");
                return SuccessResponse(request, "Service is already stopped");
            }

            _logger.LogInformation("[TIMING] Calling installer.StopAsync() (elapsed: {ElapsedMs:F0}ms)...", sw.Elapsed.TotalMilliseconds);
            await installer.StopAsync(ct);
            _logger.LogInformation("[TIMING] installer.StopAsync() completed in {ElapsedMs:F0}ms total", sw.Elapsed.TotalMilliseconds);
            _logger.LogInformation("Service stopped successfully via remote request");

            return SuccessResponse(request, "Service stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TIMING] Failed to stop service via remote request after {ElapsedMs:F0}ms", sw.Elapsed.TotalMilliseconds);
            return ErrorResponse(request, ErrorCode.InternalError, $"Failed to stop service: {ex.Message}");
        }
    }

    private IServiceInstaller? ResolveInstaller()
    {
        var installers = _serviceProvider.GetServices<IServiceInstaller>();
        return installers.FirstOrDefault(i => i.IsSupported);
    }

    private static Envelope SuccessResponse(Envelope request, string message)
    {
        var response = new StopServiceResponse { Success = true, Message = message };
        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = request.MessageType == MessageTypeCode.StartServiceRequest
                ? MessageTypeCode.StartServiceResponse
                : MessageTypeCode.StopServiceResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }

    private static Envelope ErrorResponse(Envelope request, int statusCode, string message)
    {
        var msgType = request.MessageType == MessageTypeCode.StartServiceRequest
            ? MessageTypeCode.StartServiceResponse
            : MessageTypeCode.StopServiceResponse;

        var response = new StopServiceResponse { Success = false, Message = message };
        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = msgType,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = statusCode,
            ErrorMessage = message
        };
    }
}
