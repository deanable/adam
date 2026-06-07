using System.Net;
using System.Net.Sockets;

namespace Adam.Shared.Services;

/// <summary>
/// Helpers for discovering the address clients use to reach this machine.
/// </summary>
public static class NetworkInfo
{
    /// <summary>
    /// Returns the server's primary routable IPv4 address — the address remote
    /// clients use to reach this machine — or <c>null</c> if none can be determined.
    ///
    /// Uses the "UDP connect" trick: a datagram socket is pointed at a public
    /// address so the OS selects the preferred outbound interface, then its local
    /// endpoint is read. No packets are actually sent, so this works offline and
    /// returns the LAN IP even with no internet access.
    ///
    /// Note: this is the machine's own interface address (the LAN/routable IP),
    /// not the WAN address of an upstream NAT. For a server clients connect to
    /// directly on the network, this is the correct address to publish.
    /// </summary>
    public static string? GetPrimaryIPv4Address()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            // 8.8.8.8 is a well-known routable address; connecting a UDP socket
            // does not send traffic, it just selects the outbound interface.
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint &&
                !IPAddress.IsLoopback(endPoint.Address))
            {
                return endPoint.Address.ToString();
            }
        }
        catch
        {
            // Fall through to the DNS-based fallback below.
        }

        // Fallback: first non-loopback IPv4 from the host's DNS entry.
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var address in host.AddressList)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address))
                {
                    return address.ToString();
                }
            }
        }
        catch
        {
            // Give up — caller falls back to its default host.
        }

        return null;
    }
}
