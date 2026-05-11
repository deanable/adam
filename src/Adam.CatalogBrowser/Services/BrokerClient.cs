using System.Collections.Concurrent;
using System.Net.Sockets;
using Adam.Shared.Contracts;
using Adam.Shared.Transport;

namespace Adam.CatalogBrowser.Services;

public sealed class BrokerClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<Envelope>> _pending = new();
    private CancellationTokenSource? _receiveCts;
    private bool _disposed;

    public bool IsConnected => _client?.Connected == true;

    public BrokerClient(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BrokerClient));

        await _lock.WaitAsync(ct);
        try
        {
            if (_client?.Connected == true) return;

            _client?.Dispose();
            _client = new TcpClient();
            await _client.ConnectAsync(_host, _port, ct);
            _stream = _client.GetStream();

            _receiveCts?.Cancel();
            _receiveCts = new CancellationTokenSource();
            _ = ReceiveLoopAsync(_receiveCts.Token);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _receiveCts?.Cancel();
            _stream?.Dispose();
            _client?.Dispose();
            _stream = null;
            _client = null;

            foreach (var kvp in _pending)
                kvp.Value.TrySetCanceled();
            _pending.Clear();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Envelope> SendAsync(Envelope request, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BrokerClient));

        var tcs = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.CorrelationId] = tcs;

        await _lock.WaitAsync(ct);
        try
        {
            if (_stream == null)
                throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

            await TcpFrame.SendAsync(_stream, request, ct);
        }
        finally
        {
            _lock.Release();
        }

        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        return await tcs.Task;
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_stream == null) break;

                var envelope = await TcpFrame.ReceiveAsync(_stream, ct);
                if (envelope == null) break;

                if (_pending.TryRemove(envelope.CorrelationId, out var tcs))
                    tcs.TrySetResult(envelope);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                foreach (var kvp in _pending)
                    kvp.Value.TrySetException(ex);
                _pending.Clear();
                break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _stream?.Dispose();
        _client?.Dispose();
        _lock.Dispose();
    }
}
