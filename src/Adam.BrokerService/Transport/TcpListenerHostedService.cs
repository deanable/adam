using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Transport;

public sealed class TcpListenerHostedService : BackgroundService
{
    private readonly TcpListenerService _listener;
    private readonly ILogger<TcpListenerHostedService> _logger;

    public TcpListenerHostedService(TcpListenerService listener, ILogger<TcpListenerHostedService> logger)
    {
        _listener = listener;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _listener.StartAsync(9100, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _listener.StopAsync();
        await base.StopAsync(cancellationToken);
    }
}
