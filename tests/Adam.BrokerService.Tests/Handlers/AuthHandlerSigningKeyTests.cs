using Adam.BrokerService.Handlers;
using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.BrokerService.Tests.Handlers;

/// <summary>
/// Verifies how <see cref="AuthHandler"/> resolves the JWT signing key.
/// The repository ships <c>Jwt:SigningKey</c> as the placeholder
/// <c>${ADAM_JWT_KEY}</c> so no secret is committed; these tests lock in that
/// the environment variable is still honoured and that a missing key fails fast.
/// </summary>
public sealed class AuthHandlerSigningKeyTests
{
    private const string EnvVar = "ADAM_JWT_KEY";

    /// <summary>A valid Base64 key of 32 bytes ("test-signing-key-for-testing-only-32-bytes").</summary>
    private const string ValidKey = "dGVzdC1zaWduaW5nLWtleS1mb3ItdGVzdGluZy1vbmx5LTMyLWJ5dGVz";

    [Fact]
    public void Constructor_WhenConfigUsesPlaceholder_FallsBackToEnvironmentVariable()
    {
        var config = BuildConfig(("Jwt:SigningKey", "${ADAM_JWT_KEY}"));

        WithEnvVar(ValidKey, () =>
        {
            var handler = CreateHandler(config);
            handler.Should().NotBeNull();
        });
    }

    [Fact]
    public void Constructor_WhenPlaceholderAndNoEnvironmentVariable_ThrowsWithGuidance()
    {
        var config = BuildConfig(("Jwt:SigningKey", "${ADAM_JWT_KEY}"));

        WithEnvVar(null, () =>
        {
            var act = () => CreateHandler(config);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*ADAM_JWT_KEY*");
        });
    }

    [Fact]
    public void Constructor_WhenKeyMissingEntirely_ThrowsWithGuidance()
    {
        var config = BuildConfig(("Jwt:TokenExpiryHours", "24"));

        WithEnvVar(null, () =>
        {
            var act = () => CreateHandler(config);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*ADAM_JWT_KEY*");
        });
    }

    [Fact]
    public void Constructor_WhenExplicitKeyConfigured_SucceedsRegardlessOfEnvironment()
    {
        var config = BuildConfig(("Jwt:SigningKey", ValidKey));

        WithEnvVar(null, () =>
        {
            var handler = CreateHandler(config);
            handler.Should().NotBeNull();
        });
    }

    [Fact]
    public void Constructor_WhenKeyIsNotValidBase64_ThrowsFormatGuidance()
    {
        var config = BuildConfig(("Jwt:SigningKey", "not-base64!!"));

        var act = () => CreateHandler(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Base64*");
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static AuthHandler CreateHandler(IConfiguration config)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new AuthHandler(
            services,
            NullLogger<AuthHandler>.Instance,
            config,
            new LoginRateLimiter(),
            connectionRegistry: null);
    }

    private static IConfiguration BuildConfig(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    /// <summary>
    /// Sets (or clears) the process-wide environment variable for the duration of
    /// <paramref name="act"/>, then restores the previous value.
    /// </summary>
    private static void WithEnvVar(string? value, Action act)
    {
        var previous = Environment.GetEnvironmentVariable(EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(EnvVar, value);
            act();
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVar, previous);
        }
    }
}
