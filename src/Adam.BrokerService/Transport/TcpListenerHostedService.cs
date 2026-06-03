using System.Diagnostics;
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
        var sw = Stopwatch.StartNew();
        var port = _configuration.GetValue<int>("Broker:Port", 9100);
        _logger.LogInformation("[TIMING] TcpListenerHostedService.ExecuteAsync() — starting broker service on port {Port}", port);
        _logger.LogInformation("[DIAG] Process: PID={Pid}, Name={Name}, StartTime={StartTime:O}",
            Environment.ProcessId,
            Process.GetCurrentProcess().ProcessName,
            Process.GetCurrentProcess().StartTime.ToUniversalTime());
        _logger.LogInformation("[DIAG] Config Broker:Port={Port}, Broker:Tls:Enabled={TlsEnabled}",
            port, _configuration.GetValue<bool>("Broker:Tls:Enabled", false));

        try
        {
            await _listener.StartAsync(port, stoppingToken);
            _logger.LogInformation("[TIMING] TcpListenerHostedService broker started in {ElapsedMs:F0}ms", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "[TIMING] TcpListenerHostedService failed to start after {ElapsedMs:F0}ms", sw.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[TIMING] TcpListenerHostedService.StopAsync() called");
        await _listener.StopAsync();
        _logger.LogInformation("[TIMING] TcpListenerHostedService stopped in {ElapsedMs:F0}ms", sw.Elapsed.TotalMilliseconds);
        await base.StopAsync(cancellationToken);
    }
}
