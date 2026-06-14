using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Adam.BrokerService.Handlers;

public sealed class AuthHandler
{
    private readonly SymmetricSecurityKey _signingKey;
    private readonly TimeSpan _tokenExpiry;
    private readonly LoginRateLimiter _rateLimiter;
    private readonly ConnectionRegistry? _connectionRegistry;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuthHandler> _logger;

    public AuthHandler(IServiceProvider serviceProvider, ILogger<AuthHandler> logger, IConfiguration configuration, LoginRateLimiter? rateLimiter = null, ConnectionRegistry? connectionRegistry = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _rateLimiter = rateLimiter ?? new LoginRateLimiter();
        _connectionRegistry = connectionRegistry;

        var keyBase64 = configuration["Jwt:SigningKey"] ?? Environment.GetEnvironmentVariable("ADAM_JWT_KEY");
        if (string.IsNullOrEmpty(keyBase64) || keyBase64 == "${ADAM_JWT_KEY}")
        {
            throw new InvalidOperationException(
                "JWT signing key is not configured. Set the ADAM_JWT_KEY environment variable " +
                "or the Jwt:SigningKey configuration value to a Base64-encoded key of at least 32 bytes.");
        }

        try
        {
            var keyBytes = Convert.FromBase64String(keyBase64);
            if (keyBytes.Length < 32)
            {
                logger.LogWarning("JWT signing key is less than 32 bytes; token security may be reduced.");
            }
            _signingKey = new SymmetricSecurityKey(keyBytes);
            logger.LogInformation("JWT signing key loaded from configuration.");
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is not valid Base64. Provide a Base64-encoded key of at least 32 bytes.");
        }

        _tokenExpiry = TimeSpan.FromHours(configuration.GetValue<int>("Jwt:TokenExpiryHours", 24));
    }

    public async Task<Envelope> LoginAsync(Envelope request, CancellationToken ct)
    {
        if (request.Payload == null)
            return ErrorResponse(request, ErrorCode.BadRequest, "Null payload");
        LoginRequest loginReq;
        try
        {
            loginReq = ProtoHelper.Deserialize<LoginRequest>(request.Payload.ToByteArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize {MessageType}", request.MessageType);
            return ErrorResponse(request, ErrorCode.BadRequest, "Malformed request payload");
        }
        var clientIp = request.ClientIp ?? "unknown";

        // Brute-force protection
        if (!_rateLimiter.TryAttempt(loginReq.Username, clientIp))
        {
            _logger.LogWarning("SECURITY: Rate limit exceeded for username '{Username}' from IP '{ClientIp}'. CorrelationId: {CorrelationId}",
                loginReq.Username, clientIp, request.CorrelationId);
            return ErrorResponse(request, ErrorCode.AuthDenied, "Too many login attempts. Please try again later.");
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Username == loginReq.Username, ct);

        if (user == null || !PasswordHelper.VerifyPassword(loginReq.Password, user.PasswordHash))
        {
            _logger.LogWarning("SECURITY: Failed login attempt for username '{Username}' from IP '{ClientIp}'. CorrelationId: {CorrelationId}",
                loginReq.Username, clientIp, request.CorrelationId);
            return ErrorResponse(request, ErrorCode.AuthDenied, "Invalid username or password");
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("SECURITY: Deactivated account login attempt for username '{Username}' from IP '{ClientIp}'. CorrelationId: {CorrelationId}",
                loginReq.Username, clientIp, request.CorrelationId);
            return ErrorResponse(request, ErrorCode.Forbidden, "Account is deactivated");
        }

        _logger.LogInformation("SECURITY: Successful login for username '{Username}' from IP '{ClientIp}'. CorrelationId: {CorrelationId}",
            loginReq.Username, clientIp, request.CorrelationId);

        var token = GenerateJwt(user);
        var response = new LoginResponse
        {
            Token = token,
            ExpiresAt = DateTimeOffset.UtcNow.Add(_tokenExpiry).ToUnixTimeSeconds(),
            User = new UserProfile
            {
                Id = user.Id.ToString(),
                Username = user.Username,
                Role = user.Role!.Name
            }
        };

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        // Associate connection with authenticated user for broadcast targeting
        if (_connectionRegistry != null && !string.IsNullOrEmpty(request.ConnectionId))
        {
            _connectionRegistry.SetUserId(request.ConnectionId, user.Id.ToString());
        }

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = MessageTypeCode.LoginResponse,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
            StatusCode = 0
        };
    }

    public Envelope ValidateToken(Envelope request)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        try
        {
            var principal = tokenHandler.ValidateToken(request.AuthToken, GetTokenValidationParameters(), out _);
            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            var username = principal.FindFirst(ClaimTypes.Name)?.Value ?? "";
            var jwtRole = principal.FindFirst(ClaimTypes.Role)?.Value ?? "";

            // Phase 7: Query DB for current user status and role (T7.5)
            string role = jwtRole;
            bool isValid = true;

            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var userGuid))
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var user = db.Users.Include(u => u.Role)
                        .AsNoTracking()
                        .FirstOrDefault(u => u.Id == userGuid);

                    if (user == null || !user.IsActive)
                    {
                        // Account deactivated or deleted
                        isValid = false;
                    }
                    else if (user.Role != null && !string.Equals(user.Role.Name, jwtRole, StringComparison.OrdinalIgnoreCase))
                    {
                        // Role changed in DB — return the updated role
                        role = user.Role.Name;
                        _logger.LogInformation("Role change detected for user {UserId}: JWT role '{JwtRole}' -> DB role '{DbRole}'",
                            userId, jwtRole, role);
                    }
                }
                catch (Exception ex)
                {
                    // DB query failure is non-fatal — fall back to JWT role
                    _logger.LogWarning(ex, "DB lookup failed during token validation for user {UserId}; using JWT role", userId);
                }
            }

            if (!isValid)
            {
                var invalidResponse = new ValidateTokenResponse { IsValid = false };
                return new Envelope
                {
                    CorrelationId = request.CorrelationId,
                    MessageType = MessageTypeCode.ValidateTokenResponse,
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(invalidResponse)),
                    StatusCode = ErrorCode.Forbidden
                };
            }

            var response = new ValidateTokenResponse
            {
                IsValid = true,
                User = new UserProfile { Id = userId, Username = username, Role = role }
            };

            return new Envelope
            {
                CorrelationId = request.CorrelationId,
                MessageType = MessageTypeCode.ValidateTokenResponse,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
                StatusCode = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SECURITY: Token validation failed for CorrelationId: {CorrelationId}. Reason: {Message}",
                request.CorrelationId, ex.Message);
            var response = new ValidateTokenResponse { IsValid = false };
            return new Envelope
            {
                CorrelationId = request.CorrelationId,
                MessageType = MessageTypeCode.ValidateTokenResponse,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
                StatusCode = ErrorCode.AuthDenied
            };
        }
    }

    public string GetUserId(Envelope request)
    {
        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(request.AuthToken, GetTokenValidationParameters(), out _);
            return principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        }
        catch
        {
            return "";
        }
    }

    public string GetUserRole(Envelope request)
    {
        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(request.AuthToken, GetTokenValidationParameters(), out _);
            return principal.FindFirst(ClaimTypes.Role)?.Value ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Generates a JWT token for the specified user. Primarily intended for testing.
    /// </summary>
    public string GenerateTokenForUser(User user) => GenerateJwt(user);

    private string GenerateJwt(User user)
    {
        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role!.Name),
            new Claim("jti", Guid.NewGuid().ToString("N")),
            new Claim("iat", new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: "adam-broker",
            audience: "adam-catalog",
            claims: claims,
            notBefore: now,
            expires: now.Add(_tokenExpiry),
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private TokenValidationParameters GetTokenValidationParameters()
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "adam-broker",
            ValidateAudience = true,
            ValidAudience = "adam-catalog",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    }

    private static Envelope ErrorResponse(Envelope request, int code, string message)
    {
        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = request.MessageType,
            StatusCode = code,
            ErrorMessage = message
        };
    }
}
