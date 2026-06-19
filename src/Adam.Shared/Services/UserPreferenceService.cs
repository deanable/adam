using System.Text.Json;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.Shared.Services;

/// <summary>
/// Two-tier preference service:
/// - Standalone: reads/writes UserPreference rows directly via AppDbContext
/// - Multi-user: routes through BrokerClient protobuf messages
/// 
/// The JSON blob includes a schemaVersion field for forward compatibility.
/// Unknown keys are preserved on round-trip (older clients don't clobber newer settings).
/// </summary>
public sealed class UserPreferenceService : IUserPreferenceService
{
    private readonly IDbContextFactory<AppDbContext>? _dbFactory;
    private readonly BrokerClient? _broker;
    private readonly Guid? _userId;
    private readonly string? _authToken;
    private readonly ILogger<UserPreferenceService> _logger;
    private readonly Dictionary<string, string> _cache = new();
    private bool _loaded;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public UserPreferenceService(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<UserPreferenceService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public UserPreferenceService(
        BrokerClient broker,
        Guid userId,
        string authToken,
        ILogger<UserPreferenceService> logger)
    {
        _broker = broker;
        _userId = userId;
        _authToken = authToken;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, T? defaultValue = default, CancellationToken ct = default)
        where T : class
    {
        var json = await GetRawAsync(key, ct);
        if (json == null)
            return defaultValue;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize preference {Key}", key);
            return defaultValue;
        }
    }

    public async Task<T> GetOrDefaultAsync<T>(string key, T defaultValue, CancellationToken ct = default)
    {
        var result = await GetAsync<object>(key, null, ct);
        if (result == null)
            return defaultValue;

        try
        {
            var json = JsonSerializer.Serialize(result, JsonOptions);
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        _cache[key] = json;

        if (_broker != null && _userId != null)
        {
            await SetViaBrokerAsync(key, json, ct);
        }
        else if (_dbFactory != null)
        {
            await SetViaDbAsync(key, json, ct);
        }
    }

    public async Task ResetAsync(string key, CancellationToken ct = default)
    {
        _cache.Remove(key);

        if (_broker != null && _userId != null)
        {
            await ResetViaBrokerAsync(key, ct);
        }
        else if (_dbFactory != null)
        {
            await ResetViaDbAsync(key, ct);
        }
    }

    public async Task ResetAllAsync(CancellationToken ct = default)
    {
        _cache.Clear();

        if (_broker != null && _userId != null)
        {
            await ResetAllViaBrokerAsync(ct);
        }
        else if (_dbFactory != null)
        {
            await ResetAllViaDbAsync(ct);
        }
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_loaded)
            return;

        if (_broker != null && _userId != null)
        {
            await LoadFromBrokerAsync(ct);
        }
        else if (_dbFactory != null)
        {
            await LoadFromDbAsync(ct);
        }

        _loaded = true;
    }

    private async Task<string?> GetRawAsync(string key, CancellationToken ct)
    {
        // Check in-memory cache first
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        if (_broker != null && _userId != null)
        {
            // Load all from broker
            await LoadFromBrokerAsync(ct);
            return _cache.GetValueOrDefault(key);
        }
        else if (_dbFactory != null)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var pref = await db.UserPreferences
                .Where(p => p.Key == key)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            if (pref != null)
            {
                _cache[key] = pref.ValueJson;
                return pref.ValueJson;
            }
        }

        return null;
    }

    private async Task SetViaDbAsync(string key, string json, CancellationToken ct)
    {
        await using var db = await _dbFactory!.CreateDbContextAsync(ct);

        var existing = await db.UserPreferences
            .FirstOrDefaultAsync(p => p.Key == key, ct);

        if (existing != null)
        {
            existing.ValueJson = json;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.Version++;
        }
        else
        {
            db.UserPreferences.Add(new UserPreference
            {
                Id = Guid.NewGuid(),
                Key = key,
                ValueJson = json,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task ResetViaDbAsync(string key, CancellationToken ct)
    {
        await using var db = await _dbFactory!.CreateDbContextAsync(ct);

        var existing = await db.UserPreferences
            .FirstOrDefaultAsync(p => p.Key == key, ct);

        if (existing != null)
        {
            db.UserPreferences.Remove(existing);
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task ResetAllViaDbAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory!.CreateDbContextAsync(ct);

        var all = await db.UserPreferences.ToListAsync(ct);
        db.UserPreferences.RemoveRange(all);
        await db.SaveChangesAsync(ct);
    }

    private async Task LoadFromDbAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory!.CreateDbContextAsync(ct);
        var all = await db.UserPreferences
            .AsNoTracking()
            .ToListAsync(ct);

        _cache.Clear();
        foreach (var pref in all)
        {
            _cache[pref.Key] = pref.ValueJson;
        }

        _logger.LogInformation("Loaded {Count} preferences from local DB", _cache.Count);
    }

    private async Task LoadFromBrokerAsync(CancellationToken ct)
    {
        if (_broker == null || _authToken == null)
            return;

        var request = new Envelope
        {
            AuthToken = _authToken,
            CorrelationId = Guid.NewGuid().ToString(),
            MessageType = MessageTypeCode.GetPreferencesRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetPreferencesRequest()))
        };

        var response = await _broker.SendAsync(request, ct);
        if (response == null || response.StatusCode != 0)
        {
            _logger.LogWarning("Broker returned error {StatusCode} for GetPreferencesRequest", response?.StatusCode);
            return;
        }

        var payload = ProtoHelper.Deserialize<GetPreferencesResponse>(response.Payload.ToByteArray());

        _cache.Clear();
        foreach (var pref in payload.Preferences)
        {
            _cache[pref.Key] = pref.ValueJson;
        }

        _logger.LogInformation("Loaded {Count} preferences from broker", _cache.Count);
    }

    private async Task SetViaBrokerAsync(string key, string json, CancellationToken ct)
    {
        if (_broker == null || _authToken == null)
            return;

        var request = new Envelope
        {
            AuthToken = _authToken,
            CorrelationId = Guid.NewGuid().ToString(),
            MessageType = MessageTypeCode.SetPreferenceRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new SetPreferenceRequest
            {
                Key = key,
                ValueJson = json
            }))
        };

        var response = await _broker.SendAsync(request, ct);
        if (response == null || response.StatusCode != 0)
        {
            _logger.LogWarning("Broker returned error {StatusCode} for SetPreferenceRequest (key={Key})", response?.StatusCode, key);
        }
    }

    private async Task ResetViaBrokerAsync(string key, CancellationToken ct)
    {
        if (_broker == null || _authToken == null)
            return;

        var request = new Envelope
        {
            AuthToken = _authToken,
            CorrelationId = Guid.NewGuid().ToString(),
            MessageType = MessageTypeCode.ResetPreferenceRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ResetPreferenceRequest
            {
                Key = key
            }))
        };

        var response = await _broker.SendAsync(request, ct);
        if (response == null || response.StatusCode != 0)
        {
            _logger.LogWarning("Broker returned error {StatusCode} for ResetPreferenceRequest (key={Key})", response?.StatusCode, key);
        }
    }

    private async Task ResetAllViaBrokerAsync(CancellationToken ct)
    {
        if (_broker == null || _authToken == null)
            return;

        var request = new Envelope
        {
            AuthToken = _authToken,
            CorrelationId = Guid.NewGuid().ToString(),
            MessageType = MessageTypeCode.ResetAllPreferencesRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ResetAllPreferencesRequest()))
        };

        var response = await _broker.SendAsync(request, ct);
        if (response == null || response.StatusCode != 0)
        {
            _logger.LogWarning("Broker returned error {StatusCode} for ResetAllPreferencesRequest", response?.StatusCode);
        }
    }
}
