using System.Reflection;
using Adam.Shared.Contracts;
using Adam.Shared.Services;
using FluentAssertions;

namespace Adam.Shared.Tests.Services;

/// <summary>
/// Tests for <see cref="AuthSession"/> members added in Phase 7:
/// <c>ValidateTokenAsync</c>, <c>IsTokenExpired</c>, <c>TokenExpiresAt</c>, and <c>CurrentUser</c>.
/// </summary>
public sealed class AuthSessionTests
{
    private readonly BrokerClient _broker;
    private readonly AuthSession _auth;

    public AuthSessionTests()
    {
        _broker = new BrokerClient("localhost", 9999);
        _auth = new AuthSession(_broker);
    }

    // ── Reflection helpers ─────────────────────────────────────────────

    private static void SetToken(AuthSession auth, string? token)
    {
        var field = typeof(AuthSession).GetField("<Token>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(auth, token);
    }

    private static void SetCurrentUser(AuthSession auth, UserProfile? user)
    {
        var field = typeof(AuthSession).GetField("_currentUser",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(auth, user);
    }

    private static void SetTokenExpiresAt(AuthSession auth, long expiresAt)
    {
        var field = typeof(AuthSession).GetField("<TokenExpiresAt>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(auth, expiresAt);
    }

    // ─────────────────────────────────────────────────────────────────
    //  IsLoggedIn
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void IsLoggedIn_NoToken_ReturnsFalse()
    {
        _auth.IsLoggedIn.Should().BeFalse();
    }

    [Fact]
    public void IsLoggedIn_TokenWithoutUser_ReturnsFalse()
    {
        SetToken(_auth, "some-token");
        // CurrentUser is null

        // Token setter is private, but CurrentUser backing field remains null
        // IsLoggedIn checks both Token != null && CurrentUser != null
        // Since we set Token via reflection but didn't set CurrentUser...
        // Let's use the actual public path via LoginAsync which sets both.
        // For reflection-based setup: set both.
        SetCurrentUser(_auth, null);
        _auth.IsLoggedIn.Should().BeFalse();
    }

    [Fact]
    public void IsLoggedIn_TokenAndUser_ReturnsTrue()
    {
        SetToken(_auth, "valid-token");
        SetCurrentUser(_auth, new UserProfile { Username = "test", Role = "Viewer" });

        _auth.IsLoggedIn.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────
    //  IsTokenExpired
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void IsTokenExpired_NoExpirySet_ReturnsTrue()
    {
        // TokenExpiresAt defaults to 0
        _auth.IsTokenExpired().Should().BeTrue();
    }

    [Fact]
    public void IsTokenExpired_FutureExpiry_ReturnsFalse()
    {
        SetTokenExpiresAt(_auth, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400);
        _auth.IsTokenExpired().Should().BeFalse();
    }

    [Fact]
    public void IsTokenExpired_PastExpiry_ReturnsTrue()
    {
        SetTokenExpiresAt(_auth, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600);
        _auth.IsTokenExpired().Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────
    //  CurrentUser property changed notification
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Logout_FiresCurrentUserPropertyChanged()
    {
        // Arrange: set up a logged-in session
        SetToken(_auth, "token");
        SetCurrentUser(_auth, new UserProfile { Username = "alice", Role = "Editor" });

        var changed = new List<string?>();
        _auth.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Act: Logout goes through the property setter (CurrentUser = null)
        _auth.Logout();

        // Assert: CurrentUser change fires
        changed.Should().Contain(nameof(AuthSession.CurrentUser));
    }

    // ─────────────────────────────────────────────────────────────────
    //  ValidateTokenAsync — early return paths
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateTokenAsync_NoToken_ReturnsNull()
    {
        // Token is null by default
        // BrokerClient is not connected

        var result = await _auth.ValidateTokenAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateTokenAsync_NotConnected_ReturnsNull()
    {
        SetToken(_auth, "some-token");
        SetCurrentUser(_auth, new UserProfile { Username = "test", Role = "Viewer" });
        // BrokerClient is not connected

        var result = await _auth.ValidateTokenAsync();

        result.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────
    //  Logout — clears session state
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Logout_ClearsTokenAndUserAndExpiry()
    {
        SetToken(_auth, "some-token");
        SetCurrentUser(_auth, new UserProfile { Username = "test", Role = "Viewer" });
        SetTokenExpiresAt(_auth, 1234567890);

        _auth.Logout();

        _auth.Token.Should().BeNull();
        _auth.CurrentUser.Should().BeNull();
        _auth.TokenExpiresAt.Should().Be(0);
        _auth.IsLoggedIn.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────
    //  TokenExpiresAt property
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TokenExpiresAt_Default_IsZero()
    {
        _auth.TokenExpiresAt.Should().Be(0);
    }
}
