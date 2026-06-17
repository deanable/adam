using Adam.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

/// <summary>
/// Base class for all BrokerService message handlers.
/// Provides common ErrorResponse, DeserializePayload helpers and shared DI dependencies.
/// </summary>
public abstract class HandlerBase
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly ILogger Logger;
    protected readonly AuthorizationMiddleware Authz;

    protected HandlerBase(IServiceProvider serviceProvider, ILogger logger, AuthorizationMiddleware authz)
    {
        ServiceProvider = serviceProvider;
        Logger = logger;
        Authz = authz;
    }

    /// <summary>
    /// Creates a standard error response envelope.
    /// </summary>
    protected static Envelope ErrorResponse(Envelope request, int code, string message)
    {
        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = request.MessageType,
            StatusCode = code,
            ErrorMessage = message
        };
    }

    /// <summary>
    /// Deserializes the request payload from the envelope.
    /// Returns an error envelope if the payload is null or malformed, or null on success.
    /// </summary>
    protected Envelope? DeserializePayload<T>(Envelope request, out T? payload) where T : class, IProtoSerializable, new()
    {
        payload = null;

        if (request.Payload == null)
            return ErrorResponse(request, ErrorCode.BadRequest, "Null payload");

        try
        {
            payload = ProtoHelper.Deserialize<T>(request.Payload.ToByteArray());
            return null; // success
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to deserialize {MessageType}", request.MessageType);
            return ErrorResponse(request, ErrorCode.BadRequest, "Malformed request payload");
        }
    }
}
