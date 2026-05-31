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
        try
        {
            var installer = ResolveInstaller();
            if (installer == null)
            {
                return ErrorResponse(request, 13, "No service installer available for this platform");
            }

            var status = await installer.GetStatusAsync(ct);
            if (status == ServiceStatus.NotInstalled)
            {
                return ErrorResponse(request, 5, "Service is not installed");
            }

            if (status == ServiceStatus.Running)
            {
                return SuccessResponse(request, "Service is already running");
            }

            await installer.StartAsync(ct);
            _logger.LogInformation("Service started successfully via remote request");

            return SuccessResponse(request, "Service started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start service via remote request");
            return ErrorResponse(request, 13, $"Failed to start service: {ex.Message}");
        }
    }

    public async Task<Envelope> StopServiceAsync(Envelope request, CancellationToken ct)
    {
        try
        {
            var installer = ResolveInstaller();
            if (installer == null)
            {
                return ErrorResponse(request, 13, "No service installer available for this platform");
            }

            var status = await installer.GetStatusAsync(ct);
            if (status == ServiceStatus.NotInstalled)
            {
                return ErrorResponse(request, 5, "Service is not installed");
            }

            if (status == ServiceStatus.Stopped)
            {
                return SuccessResponse(request, "Service is already stopped");
            }

            await installer.StopAsync(ct);
            _logger.LogInformation("Service stopped successfully via remote request");

            return SuccessResponse(request, "Service stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop service via remote request");
            return ErrorResponse(request, 13, $"Failed to stop service: {ex.Message}");
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
