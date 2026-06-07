using Microsoft.Win32;

namespace Adam.Shared.Configuration;

/// <summary>
/// Shared connection settings persisted to the Windows registry so that the
/// (elevated) Service Manager can publish the broker endpoint and the
/// (standard-user) Catalog Browser can read it at launch.
///
/// Stored under <c>HKEY_LOCAL_MACHINE\SOFTWARE\Adam\Connection</c> — machine-wide,
/// NOT per-user. This is deliberate: the Service Manager runs elevated (a
/// different user hive than the client), so HKCU would not be visible to the
/// client. Writing to HKLM requires administrator rights (the Service Manager
/// has them); reading does not (the client is a standard user).
/// </summary>
public sealed class RegistrySettings
{
    /// <summary>Registry sub-key path under HKLM.</summary>
    public const string KeyPath = @"SOFTWARE\Adam\Connection";

    public string ServiceHost { get; set; } = "localhost";
    public int ServicePort { get; set; } = 9100;
    public bool UseTls { get; set; }
    public bool AllowSelfSigned { get; set; } = true;

    /// <summary>
    /// Username clients should pre-fill in the login dialog. Never stores a password.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Reads the published settings from HKLM. Returns <c>null</c> when the key
    /// does not exist (nothing has been published yet) or on any failure, so
    /// callers fall back to their local defaults.
    /// </summary>
    public static RegistrySettings? Load()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
            if (key == null)
                return null;

            var host = key.GetValue("ServiceHost") as string;
            if (string.IsNullOrWhiteSpace(host))
                return null;

            return new RegistrySettings
            {
                ServiceHost = host,
                ServicePort = key.GetValue("ServicePort") is int p ? p : 9100,
                UseTls = (key.GetValue("UseTls") is int t ? t : 0) != 0,
                AllowSelfSigned = (key.GetValue("AllowSelfSigned") is int s ? s : 1) != 0,
                Username = key.GetValue("Username") as string ?? string.Empty,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the settings to HKLM. Requires administrator privileges.
    /// Throws <see cref="PlatformNotSupportedException"/> on non-Windows and
    /// <see cref="UnauthorizedAccessException"/> if not elevated.
    /// </summary>
    public void Save()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Registry settings are only supported on Windows.");

        using var key = Registry.LocalMachine.CreateSubKey(KeyPath, writable: true);
        key.SetValue("ServiceHost", ServiceHost ?? "localhost", RegistryValueKind.String);
        key.SetValue("ServicePort", ServicePort, RegistryValueKind.DWord);
        key.SetValue("UseTls", UseTls ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("AllowSelfSigned", AllowSelfSigned ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("Username", Username ?? string.Empty, RegistryValueKind.String);
    }
}
