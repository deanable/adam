using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Transport;

public sealed class TcpListenerHostedService : BackgroundService
{
    private readonly TcpListenerService _listener;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TcpListenerHostedService> _logger;

    public TcpListenerHostedService(
        TcpListenerService listener,
        IConfiguration configuration,
        ILogger<TcpListenerHostedService> logger)
    {
        _listener = listener;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = _configuration.GetValue<int>("Broker:Port", 9100);
        _logger.LogInformation("Starting broker service on configured port {Port}", port);
        await _listener.StartAsync(port, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _listener.StopAsync();
        await base.StopAsync(cancellationToken);
    }
}
