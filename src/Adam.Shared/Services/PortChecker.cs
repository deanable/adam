using System.Net;
using System.Net.Sockets;

namespace Adam.Shared.Services;

/// <summary>
/// Checks whether a local TCP port is currently in use.
/// </summary>
public static class PortChecker
{
    /// <summary>
    /// Returns true if the port is free (not currently bound by any process).
    /// </summary>
    public static bool IsPortFree(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true if the port is in use by another process.
    /// </summary>
    public static bool IsPortInUse(int port) => !IsPortFree(port);

    /// <summary>
    /// Attempts to find a free port starting from <paramref name="preferredPort"/>.
    /// Returns the first available port, or -1 if none found in the range.
    /// </summary>
    public static int FindFreePort(int preferredPort = 9100, int maxAttempts = 100)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            var candidate = preferredPort + i;
            if (candidate > ushort.MaxValue) break;
            if (IsPortFree(candidate))
                return candidate;
        }
        return -1;
    }
}
