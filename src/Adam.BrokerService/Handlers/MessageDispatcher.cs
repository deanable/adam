using System.Diagnostics;
using Adam.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

/// <summary>
/// Generic dispatcher that routes incoming envelopes to registered handler functions
/// based on <see cref="MessageTypeCode"/>. Handles null-check, unknown-type logging,
/// exception catch with timing, and error response construction.
/// </summary>
public sealed class MessageDispatcher
{
    private readonly ILogger _logger;
    private readonly Dictionary<MessageTypeCode, Func<Envelope, CancellationToken, Task<Envelope>>> _dispatch;

    /// <summary>
    /// Creates a dispatcher with the given message-type-to-handler map.
    /// </summary>
    /// <param name="dispatch">Dictionary mapping each <see cref="MessageTypeCode"/> to its async handler.</param>
    /// <param name="logger">Logger for recording dispatch errors and unknown types.</param>
    public MessageDispatcher(
        Dictionary<MessageTypeCode, Func<Envelope, CancellationToken, Task<Envelope>>> dispatch,
        ILogger logger)
    {
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Dispatches the request envelope to the registered handler for its <see cref="Envelope.MessageType"/>.
    /// </summary>
    /// <param name="request">The incoming request envelope. Must not be null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The response envelope from the handler, or an error envelope on failure.</returns>
    public async Task<Envelope> DispatchAsync(Envelope request, CancellationToken ct = default)
    {
        if (request == null)
        {
            _logger.LogWarning("Received null request envelope");
            return ErrorResponse(new Envelope(), ErrorCode.BadRequest, "Null request envelope");
        }

        var sw = Stopwatch.StartNew();
        try
        {
            if (_dispatch.TryGetValue(request.MessageType, out var handler))
                return await handler(request, ct);

            _logger.LogWarning("Unknown message type: {MessageType} from conn={ConnectionId}",
                request.MessageType, request.ConnectionId);
            return ErrorResponse(request, ErrorCode.UnknownMessageType, $"Unknown message type: {request.MessageType}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling {MessageType} from conn={ConnectionId} after {ElapsedMs:F0}ms",
                request.MessageType, request.ConnectionId, sw.Elapsed.TotalMilliseconds);
            return ErrorResponse(request, ErrorCode.InternalError, "Internal server error");
        }
    }

    private static Envelope ErrorResponse(Envelope request, int statusCode, string message)
    {
        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = request.MessageType,
            StatusCode = statusCode,
            ErrorMessage = message
        };
    }
}
