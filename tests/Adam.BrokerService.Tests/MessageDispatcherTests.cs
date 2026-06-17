using Adam.BrokerService.Handlers;
using Adam.Shared.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.BrokerService.Tests;

/// <summary>
/// Unit tests for <see cref="MessageDispatcher"/> — the generic dispatch layer
/// that routes envelopes to registered handler functions by <see cref="MessageTypeCode"/>.
///
/// These tests use inline lambda handlers and <see cref="NullLogger{T}"/>,
/// requiring no DI container or database.
/// </summary>
public sealed class MessageDispatcherTests
{
    private static readonly ILogger<MessageDispatcher> Logger = NullLogger<MessageDispatcher>.Instance;

    // ══════════════════════════════════════════════════════════════
    //  Constructor guards
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_NullDispatch_ThrowsArgumentNullException()
    {
        var act = () => new MessageDispatcher(null!, Logger);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("dispatch");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new MessageDispatcher(new Dictionary<MessageTypeCode, Func<Envelope, CancellationToken, Task<Envelope>>>(), null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    // ══════════════════════════════════════════════════════════════
    //  Null request
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task DispatchAsync_NullRequest_ReturnsBadRequest()
    {
        var dispatcher = new MessageDispatcher(new(), Logger);

        var response = await dispatcher.DispatchAsync(null!);

        response.StatusCode.Should().Be(ErrorCode.BadRequest);
        response.ErrorMessage.Should().Be("Null request envelope");
        response.CorrelationId.Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════════
    //  Unknown message type
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task DispatchAsync_UnknownMessageType_ReturnsUnknownMessageType()
    {
        var dispatcher = new MessageDispatcher(new(), Logger);

        var correlationId = Guid.NewGuid().ToString();
        var request = new Envelope
        {
            MessageType = MessageTypeCode.GetServiceStatusRequest,
            CorrelationId = correlationId
        };

        var response = await dispatcher.DispatchAsync(request);

        response.StatusCode.Should().Be(ErrorCode.UnknownMessageType);
        response.ErrorMessage.Should().Be("Unknown message type: GetServiceStatusRequest");
        response.CorrelationId.Should().Be(correlationId);
        response.MessageType.Should().Be(MessageTypeCode.GetServiceStatusRequest);
    }

    // ══════════════════════════════════════════════════════════════
    //  Successful dispatch
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task DispatchAsync_KnownType_ReturnsHandlerResult()
    {
        var map = new Dictionary<MessageTypeCode, Func<Envelope, CancellationToken, Task<Envelope>>>
        {
            [MessageTypeCode.LoginRequest] = (req, ct) => Task.FromResult(new Envelope
            {
                CorrelationId = req.CorrelationId,
                StatusCode = ErrorCode.Success,
                MessageType = MessageTypeCode.LoginResponse
            })
        };

        var dispatcher = new MessageDispatcher(map, Logger);
        var correlationId = Guid.NewGuid().ToString();
        var request = new Envelope
        {
            MessageType = MessageTypeCode.LoginRequest,
            CorrelationId = correlationId
        };

        var response = await dispatcher.DispatchAsync(request);

        response.StatusCode.Should().Be(ErrorCode.Success);
        response.CorrelationId.Should().Be(correlationId);
        response.MessageType.Should().Be(MessageTypeCode.LoginResponse);
    }

    [Fact]
    public async Task DispatchAsync_KnownType_PassesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var ctPassed = CancellationToken.None;

        var map = new Dictionary<MessageTypeCode, Func<Envelope, CancellationToken, Task<Envelope>>>
        {
            [MessageTypeCode.LoginRequest] = (req, ct) =>
            {
                ctPassed = ct;
                return Task.FromResult(new Envelope
                {
                    CorrelationId = req.CorrelationId,
                    StatusCode = ErrorCode.Success,
                    MessageType = MessageTypeCode.LoginResponse
                });
            }
        };

        var dispatcher = new MessageDispatcher(map, Logger);
        var request = new Envelope
        {
            MessageType = MessageTypeCode.LoginRequest,
            CorrelationId = "ct-test"
        };

        await dispatcher.DispatchAsync(request, cts.Token);

        ctPassed.Should().Be(cts.Token);
    }

    [Fact]
    public async Task DispatchAsync_MultipleRegistrations_EachRoutesCorrectly()
    {
        var map = new Dictionary<MessageTypeCode, Func<Envelope, CancellationToken, Task<Envelope>>>
        {
            [MessageTypeCode.ListAssetsRequest] = (req, ct) => Task.FromResult(new Envelope
            {
                CorrelationId = req.CorrelationId,
                StatusCode = 10
            }),
            [MessageTypeCode.ListCollectionsRequest] = (req, ct) => Task.FromResult(new Envelope
            {
                CorrelationId = req.CorrelationId,
                StatusCode = 20
            })
        };

        var dispatcher = new MessageDispatcher(map, Logger);

        var r1 = await dispatcher.DispatchAsync(new Envelope
        {
            MessageType = MessageTypeCode.ListAssetsRequest,
            CorrelationId = "a"
        });
        r1.StatusCode.Should().Be(10);

        var r2 = await dispatcher.DispatchAsync(new Envelope
        {
            MessageType = MessageTypeCode.ListCollectionsRequest,
            CorrelationId = "b"
        });
        r2.StatusCode.Should().Be(20);
    }

    // ══════════════════════════════════════════════════════════════
    //  Handler exception — caught and wrapped
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task DispatchAsync_HandlerThrows_ReturnsInternalError()
    {
        var map = new Dictionary<MessageTypeCode, Func<Envelope, CancellationToken, Task<Envelope>>>
        {
            [MessageTypeCode.CreateCollectionRequest] = (req, ct) =>
                throw new InvalidOperationException("DB connection lost")
        };

        var dispatcher = new MessageDispatcher(map, Logger);
        var correlationId = Guid.NewGuid().ToString();
        var request = new Envelope
        {
            MessageType = MessageTypeCode.CreateCollectionRequest,
            CorrelationId = correlationId
        };

        var response = await dispatcher.DispatchAsync(request);

        response.StatusCode.Should().Be(ErrorCode.InternalError);
        response.ErrorMessage.Should().Be("Internal server error");
        response.CorrelationId.Should().Be(correlationId);
        response.MessageType.Should().Be(MessageTypeCode.CreateCollectionRequest);
    }

    [Fact]
    public async Task DispatchAsync_HandlerThrows_OperationCanceledException_IsCaught()
    {
        // NOTE: OperationCanceledException is caught by the catch-all handler
        // and converted to an InternalError response. If the dispatch layer is
        // later changed to re-throw cancellation exceptions, this test documents
        // the current behavior and should be updated accordingly.
        var map = new Dictionary<MessageTypeCode, Func<Envelope, CancellationToken, Task<Envelope>>>
        {
            [MessageTypeCode.StopServiceRequest] = (req, ct) =>
                throw new OperationCanceledException("Cancelled")
        };

        var dispatcher = new MessageDispatcher(map, Logger);
        var request = new Envelope
        {
            MessageType = MessageTypeCode.StopServiceRequest,
            CorrelationId = "cancel-test"
        };

        var response = await dispatcher.DispatchAsync(request);

        response.StatusCode.Should().Be(ErrorCode.InternalError);
        response.ErrorMessage.Should().Be("Internal server error");
    }

    [Fact]
    public async Task DispatchAsync_HandlerThrows_AggregateException_IsCaught()
    {
        var map = new Dictionary<MessageTypeCode, Func<Envelope, CancellationToken, Task<Envelope>>>
        {
            [MessageTypeCode.DeleteAssetRequest] = (req, ct) =>
                throw new AggregateException("Multiple failures",
                    new InvalidOperationException("Inner failure 1"),
                    new ArgumentException("Inner failure 2"))
        };

        var dispatcher = new MessageDispatcher(map, Logger);
        var request = new Envelope
        {
            MessageType = MessageTypeCode.DeleteAssetRequest,
            CorrelationId = "agg-ex"
        };

        var response = await dispatcher.DispatchAsync(request);

        response.StatusCode.Should().Be(ErrorCode.InternalError);
        response.ErrorMessage.Should().Be("Internal server error");
    }

    // ══════════════════════════════════════════════════════════════
    //  CorrelationId propagation on error paths
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task DispatchAsync_UnknownType_PreservesRequestCorrelationId()
    {
        var dispatcher = new MessageDispatcher(new(), Logger);
        var corrId = "corr-123-abc";
        var request = new Envelope
        {
            MessageType = MessageTypeCode.ListRolesRequest,
            CorrelationId = corrId
        };

        var response = await dispatcher.DispatchAsync(request);

        response.CorrelationId.Should().Be(corrId);
    }

    [Fact]
    public async Task DispatchAsync_HandlerException_PreservesRequestCorrelationId()
    {
        var map = new Dictionary<MessageTypeCode, Func<Envelope, CancellationToken, Task<Envelope>>>
        {
            [MessageTypeCode.RefreshSmartCollectionRequest] = (req, ct) =>
                throw new Exception("oops")
        };

        var dispatcher = new MessageDispatcher(map, Logger);
        var corrId = "corr-456-def";
        var request = new Envelope
        {
            MessageType = MessageTypeCode.RefreshSmartCollectionRequest,
            CorrelationId = corrId
        };

        var response = await dispatcher.DispatchAsync(request);

        response.CorrelationId.Should().Be(corrId);
    }
}
