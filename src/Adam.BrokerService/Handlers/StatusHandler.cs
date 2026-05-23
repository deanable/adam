using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
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
}
