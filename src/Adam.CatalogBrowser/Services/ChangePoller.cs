using Adam.Shared.Contracts;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.Services;

public sealed class ChangePoller : IDisposable
{
    private readonly BrokerClient _broker;
    private readonly AuthSession _auth;
    private readonly ILogger<ChangePoller>? _logger;
    private Timer? _timer;
    private long _lastPollTimestamp;

    public event Action<List<ChangeEvent>>? ChangesDetected;
    public event Action<string>? ErrorOccurred;
    public bool IsRunning { get; private set; }

    public ChangePoller(BrokerClient broker, AuthSession auth, ILogger<ChangePoller>? logger = null)
    {
        _broker = broker;
        _auth = auth;
        _logger = logger;
        _lastPollTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _timer = new Timer(async _ => await PollAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        _logger?.LogInformation("Change poller started (5s interval)");
    }

    public void Stop()
    {
        IsRunning = false;
        _timer?.Dispose();
        _timer = null;
        _logger?.LogInformation("Change poller stopped");
    }

    public void ResetTimestamp()
    {
        _lastPollTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private async Task PollAsync()
    {
        if (!_broker.IsConnected || !_auth.IsLoggedIn)
        {
            Stop();
            ErrorOccurred?.Invoke("Not connected or not logged in");
            return;
        }

        try
        {
            var correlationId = Guid.NewGuid().ToString();
            var request = new Envelope
            {
                AuthToken = _auth.Token ?? "",
                CorrelationId = correlationId,
                MessageType = nameof(GetChangesRequest),
                Payload = ByteString.CopyFrom(
                    ProtoHelper.Serialize(new GetChangesRequest { SinceTimestamp = _lastPollTimestamp }))
            };

            var response = await _broker.SendAsync(request);

            if (response.StatusCode == 0)
            {
                var changes = ProtoHelper.Deserialize<GetChangesResponse>(response.Payload.ToByteArray());
                if (changes.Changes.Count > 0)
                {
                    _lastPollTimestamp = changes.Changes.Max(c => c.Timestamp);
                    ChangesDetected?.Invoke(changes.Changes);
                    _logger?.LogDebug("Detected {Count} change(s)", changes.Changes.Count);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Change poll error");
            ErrorOccurred?.Invoke(ex.Message);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
