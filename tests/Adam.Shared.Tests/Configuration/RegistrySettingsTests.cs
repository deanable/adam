using System.Security.Principal;
using Adam.Shared.Configuration;
using FluentAssertions;
using Microsoft.Win32;

namespace Adam.Shared.Tests.Configuration;

/// <summary>
/// Tests for <see cref="RegistrySettings"/>, the HKLM-backed connection settings
/// shared between the (elevated) Service Manager and the (standard-user) client.
///
/// The HKLM round-trip test only runs on Windows when the test process is
/// elevated; otherwise it is skipped, since writing to HKLM requires admin.
/// </summary>
public sealed class RegistrySettingsTests
{
    private static bool IsElevatedWindows()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [Fact]
    public void Load_OnNonWindows_ReturnsNull()
    {
        if (OperatingSystem.IsWindows())
            return; // Not applicable on Windows.

        RegistrySettings.Load().Should().BeNull();
    }

    [Fact]
    public void KeyPath_IsMachineWideUnderSoftwareAdam()
    {
        // The path must be under HKLM\SOFTWARE so an elevated writer and a
        // standard-user reader share the same key.
        RegistrySettings.KeyPath.Should().Be(@"SOFTWARE\Adam\Connection");
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllValues()
    {
        if (!IsElevatedWindows())
            return; // Requires elevation to write HKLM; skip otherwise.

        // Save under a throwaway value-set, then restore the original key state.
        var original = RegistrySettings.Load();
        try
        {
            var settings = new RegistrySettings
            {
                ServiceHost = "192.168.1.50",
                ServicePort = 9443,
                UseTls = true,
                AllowSelfSigned = false,
                Username = "operator",
            };
            settings.Save();

            var loaded = RegistrySettings.Load();
            loaded.Should().NotBeNull();
            loaded!.ServiceHost.Should().Be("192.168.1.50");
            loaded.ServicePort.Should().Be(9443);
            loaded.UseTls.Should().BeTrue();
            loaded.AllowSelfSigned.Should().BeFalse();
            loaded.Username.Should().Be("operator");
        }
        finally
        {
            // Restore prior state so we don't pollute the machine.
            if (original != null)
                original.Save();
            else if (OperatingSystem.IsWindows())
                Registry.LocalMachine.DeleteSubKey(RegistrySettings.KeyPath, throwOnMissingSubKey: false);
        }
    }
}
