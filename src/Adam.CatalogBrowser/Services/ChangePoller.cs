using Adam.Shared.Contracts;
using Adam.Shared.Services;
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
    private int _consecutiveErrors;
    private const int MaxRetries = 3;
    private readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4)
    };

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
        _timer = new Timer(async _ => await PollAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
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

        // Token expiration check
        if (_auth.IsTokenExpired())
        {
            Stop();
            ErrorOccurred?.Invoke("Session expired — please log in again");
            return;
        }

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var correlationId = Guid.NewGuid().ToString();
                var request = new Envelope
                {
                    AuthToken = _auth.Token ?? "",
                    CorrelationId = correlationId,
                    MessageType = MessageTypeCode.GetChangesRequest,
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
                    _consecutiveErrors = 0; // Reset on success
                    return;
                }
                else if (response.StatusCode == 16)
                {
                    // Auth failure — stop poller, don't retry
                    Stop();
                    ErrorOccurred?.Invoke("Authentication failed — please log in again");
                    return;
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Not connected"))
            {
                // Non-retryable
                Stop();
                ErrorOccurred?.Invoke(ex.Message);
                return;
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < MaxRetries)
            {
                _logger?.LogWarning(ex, "Change poll error (attempt {Attempt}/{Max}), retrying...", attempt + 1, MaxRetries);
                await Task.Delay(RetryDelays[attempt]);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Change poll error (attempt {Attempt}/{Max}), giving up.", attempt + 1, MaxRetries);
                _consecutiveErrors++;
                ErrorOccurred?.Invoke(ex.Message);
                return;
            }
        }
    }

    private static bool IsRetryable(Exception ex)
    {
        return ex is System.IO.IOException
            or System.Net.Sockets.SocketException
            or TimeoutException
            or TaskCanceledException;
    }

    public void Dispose()
    {
        Stop();
    }
}
