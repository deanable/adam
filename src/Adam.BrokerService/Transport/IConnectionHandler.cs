using Adam.Shared.Contracts;

namespace Adam.BrokerService.Transport;

public interface IConnectionHandler
{
    Task<Envelope> HandleAsync(Envelope request, CancellationToken ct = default);
}
