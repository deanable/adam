using System.Net;
using System.Net.Sockets;
using Adam.Shared.Contracts;

namespace Adam.Shared.Transport;

public static class TcpFrame
{
    public static async Task SendAsync(NetworkStream stream, Envelope envelope, CancellationToken ct = default)
    {
        var payload = ProtoHelper.Serialize(envelope);
        var lengthBuffer = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(payload.Length));

        await stream.WriteAsync(lengthBuffer, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<Envelope?> ReceiveAsync(NetworkStream stream, CancellationToken ct = default)
    {
        var lengthBuffer = new byte[4];
        int bytesRead = 0;
        while (bytesRead < 4)
        {
            int n = await stream.ReadAsync(lengthBuffer.AsMemory(bytesRead, 4 - bytesRead), ct).ConfigureAwait(false);
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
            int n = await stream.ReadAsync(payloadBuffer.AsMemory(bytesRead, payloadLength - bytesRead), ct).ConfigureAwait(false);
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
