namespace Adam.Shared.Services;

public enum ServiceStatus
{
    NotInstalled,
    Running,
    Stopped,
    Unknown
}

public interface IServiceInstaller
{
    string ServiceName { get; }
    Task InstallAsync(string brokerPath, int port, CancellationToken ct = default);
    Task UninstallAsync(CancellationToken ct = default);
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default);
    bool IsSupported { get; }
}
