using System.Security.Cryptography;

namespace Adam.Shared.Services;

/// <summary>
/// Centralized password hashing and verification using PBKDF2 with SHA-256.
/// Both server-side (BrokerService) and client-side (CatalogBrowser standalone mode)
/// should use this helper to ensure password algorithm consistency.
/// </summary>
public static class PasswordHelper
{
    private const int SaltSize = 32;
    private const int HashSize = 32;
    private const int Iterations = 600_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// Hashes a password using PBKDF2 and returns a string in the format "salt:hash" (both Base64-encoded).
    /// </summary>
    public static string HashPassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Verifies a password against a hash string produced by <see cref="HashPassword"/>.
    /// </summary>
    public static bool VerifyPassword(string password, string hash)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(hash);

        var parts = hash.Split(':');
        if (parts.Length != 2) return false;

        byte[] salt;
        try
        {
            salt = Convert.FromBase64String(parts[0]);
        }
        catch (FormatException)
        {
            return false;
        }

        var storedHash = parts[1];
        var computedHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);
        return CryptographicOperations.FixedTimeEquals(computedHash, Convert.FromBase64String(storedHash));
    }
}
