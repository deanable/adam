using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Adam.BrokerService.Handlers;

public sealed class AuthHandler
{
    private static readonly byte[] JwtKey = RandomNumberGenerator.GetBytes(32);
    private static readonly TimeSpan TokenExpiry = TimeSpan.FromHours(24);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuthHandler> _logger;

    public AuthHandler(IServiceProvider serviceProvider, ILogger<AuthHandler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<Envelope> LoginAsync(Envelope request, CancellationToken ct)
    {
        var loginReq = ProtoHelper.Deserialize<LoginRequest>(request.Payload.ToByteArray());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Username == loginReq.Username, ct);

        if (user == null || !VerifyPassword(loginReq.Password, user.PasswordHash))
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
            ExpiresAt = DateTimeOffset.UtcNow.Add(TokenExpiry).ToUnixTimeSeconds(),
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

    public static string GetUserId(Envelope request)
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

    public static string GetUserRole(Envelope request)
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

    private static string GenerateJwt(User user)
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
            expires: DateTime.UtcNow.Add(TokenExpiry),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(JwtKey),
                SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static TokenValidationParameters GetTokenValidationParameters()
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "adam-broker",
            ValidateAudience = true,
            ValidAudience = "adam-catalog",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(JwtKey),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    }

    private static bool VerifyPassword(string password, string hash)
    {
        var parts = hash.Split(':');
        if (parts.Length != 2) return false;
        var salt = Convert.FromBase64String(parts[0]);
        var storedHash = parts[1];
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, 600_000, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(hashBytes) == storedHash;
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
