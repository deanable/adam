using System.Net;
using System.Net.Sockets;
using Adam.Shared.Contracts;

namespace Adam.Shared.Transport;

public static class TcpFrame
{
    /// <summary>
    /// Default timeout for receiving data from a peer (5 minutes).
    /// Prevents hanging indefinitely if a peer sends a length header but no payload.
    /// </summary>
    public const int DefaultReceiveTimeoutMs = 300_000;

    /// <summary>
    /// Default timeout for sending data to a peer (30 seconds).
    /// Prevents blocking indefinitely if a peer stops reading.
    /// </summary>
    public const int DefaultSendTimeoutMs = 30_000;

    public static async Task SendAsync(Stream stream, Envelope envelope, int sendTimeoutMs = DefaultSendTimeoutMs, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(sendTimeoutMs);
        var linkedCt = timeoutCts.Token;

        var payload = ProtoHelper.Serialize(envelope);
        var lengthBuffer = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(payload.Length));

        await stream.WriteAsync(lengthBuffer, linkedCt).ConfigureAwait(false);
        await stream.WriteAsync(payload, linkedCt).ConfigureAwait(false);
        await stream.FlushAsync(linkedCt).ConfigureAwait(false);
    }

    public static async Task<Envelope?> ReceiveAsync(Stream stream, int receiveTimeoutMs = DefaultReceiveTimeoutMs, CancellationToken ct = default)
    {
        // Apply a read timeout so we don't hang forever if a peer disconnects mid-frame
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(receiveTimeoutMs);
        var linkedCt = timeoutCts.Token;

        var lengthBuffer = new byte[4];
        int bytesRead = 0;
        while (bytesRead < 4)
        {
            int n = await stream.ReadAsync(lengthBuffer.AsMemory(bytesRead, 4 - bytesRead), linkedCt).ConfigureAwait(false);
            if (n == 0) return null;
            bytesRead += n;
        }

        int payloadLength = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lengthBuffer, 0));
        if (payloadLength < 0 || payloadLength > 256 * 1024 * 1024)
            throw new InvalidOperationException($"Invalid payload length: {payloadLength}");

        var payloadBuffer = new byte[payloadLength];
        bytesRead = 0;
        while (bytesRead < payloadLength)
        {
            int n = await stream.ReadAsync(payloadBuffer.AsMemory(bytesRead, payloadLength - bytesRead), linkedCt).ConfigureAwait(false);
            if (n == 0) return null;
            bytesRead += n;
        }

        return ProtoHelper.Deserialize<Envelope>(payloadBuffer);
    }

    public static byte[] Serialize(Envelope envelope)
    {
        var payload = ProtoHelper.Serialize(envelope);
        var lengthBuffer = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(payload.Length));
        var result = new byte[4 + payload.Length];
        Buffer.BlockCopy(lengthBuffer, 0, result, 0, 4);
        Buffer.BlockCopy(payload, 0, result, 4, payload.Length);
        return result;
    }

    public static Envelope? Deserialize(byte[] buffer)
    {
        if (buffer.Length < 4) return null;
        int payloadLength = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(buffer, 0));
        if (buffer.Length < 4 + payloadLength) return null;
        var payloadBuffer = new byte[payloadLength];
        Buffer.BlockCopy(buffer, 4, payloadBuffer, 0, payloadLength);
        return ProtoHelper.Deserialize<Envelope>(payloadBuffer);
    }
}
