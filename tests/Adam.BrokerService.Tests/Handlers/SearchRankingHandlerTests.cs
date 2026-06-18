using Adam.BrokerService.Handlers;
using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.BrokerService.Tests.Handlers;

/// <summary>
/// Tests for SearchRankingHandler covering broker-side click logging and re-ranking.
/// Uses DI with SQLite and seeded assets for isolated per-test databases.
/// </summary>
public sealed class SearchRankingHandlerTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private ServiceProvider _serviceProvider = null!;
    private SearchRankingHandler _handler = null!;
    private string _validToken = null!;
    private Guid _assetId;

    public SearchRankingHandlerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");
    }

    public async Task InitializeAsync()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "dGVzdC1zaWduaW5nLWtleS1mb3ItdGVzdGluZy1vbmx5LTMyLWJ5dGVz",
                ["Jwt:TokenExpiryHours"] = "24"
            }!)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlite($"Data Source={_dbPath}"));

        services.AddSingleton<SearchRankingService>();
        services.AddSingleton<AuthHandler>();
        services.AddSingleton<LoginRateLimiter>();
        services.AddSingleton<AuthorizationMiddleware>();
        services.AddSingleton<SearchRankingService>();
        services.AddSingleton<IDbContextFactory<AppDbContext>>(
            _ => new TestDbContextFactory(_dbPath));
        services.AddSingleton<SearchRankingHandler>();

        _serviceProvider = services.BuildServiceProvider();

        // Ensure DB created and seed a test user + asset
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();

            // Seed an admin user with a valid auth token
            var adminRole = db.Roles.FirstOrDefault(r => r.Name == "Administrator");
            if (adminRole == null)
            {
                adminRole = new Role
                {
                    Id = Guid.NewGuid(),
                    Name = "Administrator",
                    RolePermissions =
                    [
                        new RolePermission { Permission = "asset:*" },
                        new RolePermission { Permission = "collection:*" }
                    ]
                };
                db.Roles.Add(adminRole);
                await db.SaveChangesAsync();
            }

            var adminUser = new User
            {
                Id = Guid.NewGuid(),
                Username = "admin",
                Email = "admin@test.com",
                PasswordHash = PasswordHelper.HashPassword("admin123"),
                RoleId = adminRole.Id,
                IsActive = true
            };
            db.Users.Add(adminUser);
            await db.SaveChangesAsync();

            // Generate token via AuthHandler
            var authHandler = new AuthHandler(
                _serviceProvider,
                NullLogger<AuthHandler>.Instance,
                _serviceProvider.GetRequiredService<IConfiguration>());
            _validToken = authHandler.GenerateTokenForUser(adminUser);

            // Seed a test asset
            _assetId = Guid.NewGuid();
            db.DigitalAssets.Add(new DigitalAsset
            {
                Id = _assetId,
                FileName = "test.jpg",
                FileExtension = ".jpg",
                MimeType = "image/jpeg",
                FileSize = 1024,
                ChecksumSha256 = new string('a', 64),
                StoragePath = "/test/test.jpg",
                OriginalPath = "/test/test.jpg",
                Title = "Test Asset",
                Type = AssetType.Image,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            });

            var asset2 = Guid.NewGuid();
            db.DigitalAssets.Add(new DigitalAsset
            {
                Id = asset2,
                FileName = "photo.jpg",
                FileExtension = ".jpg",
                MimeType = "image/jpeg",
                FileSize = 2048,
                ChecksumSha256 = new string('b', 64),
                StoragePath = "/test/photo.jpg",
                OriginalPath = "/test/photo.jpg",
                Title = "Photo Asset",
                Type = AssetType.Image,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();
        }

        _handler = _serviceProvider.GetRequiredService<SearchRankingHandler>();
    }

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        try { File.Delete(_dbPath); } catch { }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    // ═══════════════════════════════════════════════════════
    //  LogClickAsync Handler
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task LogClickAsync_ValidRequest_ReturnsSuccessWithId()
    {
        var request = CreateRequest(MessageTypeCode.LogSearchClickRequest, new LogSearchClickRequest
        {
            AssetId = _assetId.ToString(),
            QueryText = "sunset",
            RankPosition = 1,
            DwellTimeMs = 1500
        }, _validToken);

        var response = await _handler.LogClickAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.StatusCode.Should().Be(0);
        response.MessageType.Should().Be(MessageTypeCode.LogSearchClickResponse);

        var result = ProtoHelper.Deserialize<LogSearchClickResponse>(response.Payload.ToByteArray());
        result.Id.Should().NotBeNullOrEmpty();
        Guid.TryParse(result.Id, out _).Should().BeTrue();
    }

    [Fact]
    public async Task LogClickAsync_WithoutAuth_ReturnsForbidden()
    {
        var request = CreateRequest(MessageTypeCode.LogSearchClickRequest, new LogSearchClickRequest
        {
            AssetId = _assetId.ToString(),
            QueryText = "test",
            RankPosition = 0,
            DwellTimeMs = 0
        }, "");

        var response = await _handler.LogClickAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(7); // Forbidden
    }

    [Fact]
    public async Task LogClickAsync_InvalidAssetId_ReturnsInvalidArgument()
    {
        var request = CreateRequest(MessageTypeCode.LogSearchClickRequest, new LogSearchClickRequest
        {
            AssetId = "not-a-guid",
            QueryText = "test",
            RankPosition = 0,
            DwellTimeMs = 0
        }, _validToken);

        var response = await _handler.LogClickAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be((int)ErrorCode.InvalidArgument);
    }

    [Fact]
    public async Task LogClickAsync_MalformedPayload_ReturnsError()
    {
        var request = new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _validToken,
            MessageType = MessageTypeCode.LogSearchClickRequest,
            Payload = ByteString.CopyFrom(new byte[] { 0xFF, 0xFE })
        };

        var response = await _handler.LogClickAsync(request, CancellationToken.None);

        response.StatusCode.Should().NotBe(0); // Error
    }

    // ═══════════════════════════════════════════════════════
    //  ReRankAsync Handler
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task ReRankAsync_ValidRequest_ReturnsRankedResults()
    {
        var asset2 = Guid.NewGuid();
        await using (var db = await CreateFreshDbContextAsync())
        {
            db.DigitalAssets.Add(new DigitalAsset
            {
                Id = asset2,
                FileName = "other.jpg",
                FileExtension = ".jpg",
                MimeType = "image/jpeg",
                FileSize = 500,
                ChecksumSha256 = "otherhash",
                StoragePath = "/test/other.jpg",
                OriginalPath = "/test/other.jpg",
                Title = "Other",
                Type = AssetType.Image,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var request = CreateRequest(MessageTypeCode.ReRankRequest, new ReRankRequest
        {
            Query = "test",
            Results =
            {
                new RankedAssetWire { AssetId = _assetId.ToString(), OriginalScore = 0.9f },
                new RankedAssetWire { AssetId = asset2.ToString(), OriginalScore = 0.7f }
            }
        }, _validToken);

        var response = await _handler.ReRankAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.StatusCode.Should().Be(0);
        response.MessageType.Should().Be(MessageTypeCode.ReRankResponse);

        var result = ProtoHelper.Deserialize<ReRankResponse>(response.Payload.ToByteArray());
        result.Results.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReRankAsync_WithoutAuth_ReturnsForbidden()
    {
        var request = CreateRequest(MessageTypeCode.ReRankRequest, new ReRankRequest
        {
            Query = "test",
            Results = { new RankedAssetWire { AssetId = _assetId.ToString(), OriginalScore = 0.5f } }
        }, "");

        var response = await _handler.ReRankAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(7);
    }

    [Fact]
    public async Task ReRankAsync_EmptyResults_ReturnsEmpty()
    {
        var request = CreateRequest(MessageTypeCode.ReRankRequest, new ReRankRequest
        {
            Query = "test"
        }, _validToken);

        var response = await _handler.ReRankAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(0);
        var result = ProtoHelper.Deserialize<ReRankResponse>(response.Payload.ToByteArray());
        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task ReRankAsync_MalformedPayload_ReturnsError()
    {
        var request = new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _validToken,
            MessageType = MessageTypeCode.ReRankRequest,
            Payload = ByteString.CopyFrom(new byte[] { 0xDE, 0xAD })
        };

        var response = await _handler.ReRankAsync(request, CancellationToken.None);

        response.StatusCode.Should().NotBe(0);
    }

    // ═══════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════

    private static Envelope CreateRequest<T>(MessageTypeCode type, T payload, string token)
        where T : IProtoSerializable
    {
        return new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = token,
            MessageType = type,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(payload))
        };
    }

    private async Task<AppDbContext> CreateFreshDbContextAsync()
    {
        var factory = new TestDbContextFactory(_dbPath);
        return await factory.CreateDbContextAsync();
    }

    /// <summary>
    /// Factory that creates isolated AppDbContext instances pointing to the test SQLite database.
    /// </summary>
    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly string _dbPath;

        public TestDbContextFactory(string dbPath) => _dbPath = dbPath;

        public AppDbContext CreateDbContext()
        {
            var opts = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={_dbPath}")
                .Options;
            return new AppDbContext(opts);
        }

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(CreateDbContext());
    }
}
