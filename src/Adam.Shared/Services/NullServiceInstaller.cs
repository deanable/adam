using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.Shared.Services;

public sealed class NullServiceInstaller : IServiceInstaller
{
    private readonly ILogger _logger;

    public string ServiceName => "None";
    public bool IsSupported => false;

    public NullServiceInstaller(ILogger<NullServiceInstaller>? logger = null)
    {
        _logger = logger ?? NullLogger<NullServiceInstaller>.Instance;
    }

    public Task InstallAsync(string brokerPath, int port, CancellationToken ct = default)
    {
        _logger.LogWarning("NullServiceInstaller.InstallAsync() — no platform installer found, throwing PlatformNotSupportedException");
        throw new PlatformNotSupportedException("No service installer available for this platform.");
    }
    public Task UninstallAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("NullServiceInstaller.UninstallAsync() — no platform installer found, throwing PlatformNotSupportedException");
        throw new PlatformNotSupportedException("No service installer available for this platform.");
    }
    public Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("NullServiceInstaller.StartAsync() — no platform installer found, throwing PlatformNotSupportedException");
        throw new PlatformNotSupportedException("No service installer available for this platform.");
    }
    public Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("NullServiceInstaller.StopAsync() — no platform installer found, throwing PlatformNotSupportedException");
        throw new PlatformNotSupportedException("No service installer available for this platform.");
    }
    public Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("NullServiceInstaller.GetStatusAsync() — returning NotInstalled");
        return Task.FromResult(ServiceStatus.NotInstalled);
    }
}
