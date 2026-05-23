using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
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

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuthHandler> _logger;

    public AuthHandler(IServiceProvider serviceProvider, ILogger<AuthHandler> logger, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

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
        var loginReq = ProtoHelper.Deserialize<LoginRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Username == loginReq.Username, ct);

        if (user == null || !PasswordHelper.VerifyPassword(loginReq.Password, user.PasswordHash))
        {
            return ErrorResponse(request, 16, "Invalid username or password");
        }

        if (!user.IsActive)
        {
            return ErrorResponse(request, 7, "Account is deactivated");
        }

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

        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            MessageType = nameof(LoginResponse),
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
            var role = principal.FindFirst(ClaimTypes.Role)?.Value ?? "";

            var response = new ValidateTokenResponse
            {
                IsValid = true,
                User = new UserProfile { Id = userId, Username = username, Role = role }
            };

            return new Envelope
            {
                CorrelationId = request.CorrelationId,
                MessageType = nameof(ValidateTokenResponse),
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
                StatusCode = 0
            };
        }
        catch
        {
            var response = new ValidateTokenResponse { IsValid = false };
            return new Envelope
            {
                CorrelationId = request.CorrelationId,
                MessageType = nameof(ValidateTokenResponse),
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(response)),
                StatusCode = 16
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

    private string GenerateJwt(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role!.Name)
        };

        var token = new JwtSecurityToken(
            issuer: "adam-broker",
            audience: "adam-catalog",
            claims: claims,
            expires: DateTime.UtcNow.Add(_tokenExpiry),
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
