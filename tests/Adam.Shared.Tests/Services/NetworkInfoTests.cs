using System.Net;
using Adam.Shared.Services;
using FluentAssertions;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Tests for <see cref="NetworkInfo"/>, which discovers the server's routable
/// address used when publishing the endpoint to clients.
/// </summary>
public sealed class NetworkInfoTests
{
    [Fact]
    public void GetPrimaryIPv4Address_WhenReturned_IsAValidNonLoopbackIPv4()
    {
        var result = NetworkInfo.GetPrimaryIPv4Address();

        // On an isolated build agent with no network, null is acceptable.
        if (result == null)
            return;

        IPAddress.TryParse(result, out var parsed).Should().BeTrue(
            "the returned value should be a parseable IP address");
        parsed!.AddressFamily.Should().Be(System.Net.Sockets.AddressFamily.InterNetwork);
        IPAddress.IsLoopback(parsed).Should().BeFalse(
            "a loopback address is not reachable by remote clients");
    }
}
