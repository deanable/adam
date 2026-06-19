using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Adam.BrokerService.Handlers;

/// <summary>
/// Handles user preference operations scoped to the authenticated user.
/// All operations derive the UserId from the envelope's auth token.
/// </summary>
public sealed class PreferenceHandler : HandlerBase
{
    public PreferenceHandler(
        IServiceProvider serviceProvider,
        ILogger<PreferenceHandler> logger,
        AuthorizationMiddleware authz)
        : base(serviceProvider, logger, authz)
    {
    }

    /// <summary>
    /// Returns all preferences for the current user.
    /// </summary>
    public async Task<Envelope> GetPreferencesAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var userId = ExtractUserId(request);
        if (userId == null)
            return ErrorResponse(request, 16, "Unauthenticated");

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var prefs = await db.UserPreferences
            .Where(p => p.UserId == userId.Value)
            .AsNoTracking()
            .ToListAsync(ct);

        var result = new GetPreferencesResponse();
        foreach (var pref in prefs)
        {
            result.Preferences.Add(new PreferenceItem
            {
                Key = pref.Key,
                ValueJson = pref.ValueJson,
                UpdatedAt = pref.UpdatedAt.ToUnixTimeMilliseconds(),
                Version = (int)pref.Version
            });
        }

        return CreateResponse(request, result);
    }

    /// <summary>
    /// Creates or updates a preference for the current user.
    /// </summary>
    public async Task<Envelope> SetPreferenceAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var userId = ExtractUserId(request);
        if (userId == null)
            return ErrorResponse(request, 16, "Unauthenticated");

        var error = DeserializePayload(request, out SetPreferenceRequest? payload);
        if (error != null) return error;
        if (payload == null || string.IsNullOrWhiteSpace(payload.Key))
            return ErrorResponse(request, (int)ErrorCode.InvalidArgument, "Key is required");

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.UserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId.Value && p.Key == payload.Key, ct);

        if (existing != null)
        {
            existing.ValueJson = payload.ValueJson;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.Version++;
        }
        else
        {
            db.UserPreferences.Add(new UserPreference
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                Key = payload.Key,
                ValueJson = payload.ValueJson,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1
            });
        }

        await db.SaveChangesAsync(ct);
        return CreateResponse(request, new SetPreferenceResponse());
    }

    /// <summary>
    /// Deletes a specific preference by key for the current user.
    /// </summary>
    public async Task<Envelope> ResetPreferenceAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var userId = ExtractUserId(request);
        if (userId == null)
            return ErrorResponse(request, 16, "Unauthenticated");

        var error = DeserializePayload(request, out ResetPreferenceRequest? payload);
        if (error != null) return error;
        if (payload == null || string.IsNullOrWhiteSpace(payload.Key))
            return ErrorResponse(request, (int)ErrorCode.InvalidArgument, "Key is required");

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.UserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId.Value && p.Key == payload.Key, ct);

        if (existing != null)
            db.UserPreferences.Remove(existing);

        await db.SaveChangesAsync(ct);
        return CreateResponse(request, new ResetPreferenceResponse());
    }

    /// <summary>
    /// Deletes all preferences for the current user.
    /// </summary>
    public async Task<Envelope> ResetAllPreferencesAsync(Envelope request, CancellationToken ct)
    {
        if (!await Authz.HasPermissionAsync(request, "asset:read", ct))
            return ErrorResponse(request, 7, "Forbidden");

        var userId = ExtractUserId(request);
        if (userId == null)
            return ErrorResponse(request, 16, "Unauthenticated");

        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var prefs = await db.UserPreferences
            .Where(p => p.UserId == userId.Value)
            .ToListAsync(ct);

        db.UserPreferences.RemoveRange(prefs);
        await db.SaveChangesAsync(ct);

        return CreateResponse(request, new ResetAllPreferencesResponse());
    }

    private static Guid? ExtractUserId(Envelope request)
    {
        try
        {
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(request.AuthToken);
            var sub = token.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
            if (Guid.TryParse(sub, out var userId))
                return userId;
        }
        catch
        {
        }
        return null;
    }

    private static Envelope CreateResponse<T>(Envelope request, T payload) where T : IProtoSerializable
    {
        return new Envelope
        {
            CorrelationId = request.CorrelationId,
            AuthToken = request.AuthToken,
            MessageType = (MessageTypeCode)((ushort)request.MessageType + 1),
            StatusCode = 0,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(payload))
        };
    }
}
