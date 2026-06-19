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
/// Tests for FaceHandler covering detection requests, auth gating, and edge cases.
/// Uses DI with SQLite and seeded data for isolated per-test databases.
/// </summary>
public sealed class FaceHandlerTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private ServiceProvider _serviceProvider = null!;
    private FaceHandler _handler = null!;
    private string _validToken = null!;
    private Guid _assetId;

    public FaceHandlerTests()
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

        services.AddSingleton<AuthHandler>();
        services.AddSingleton<LoginRateLimiter>();
        services.AddSingleton<AuthorizationMiddleware>();
        services.AddSingleton<IDbContextFactory<AppDbContext>>(
            _ => new TestDbContextFactory(_dbPath));
        services.AddSingleton<FaceHandler>();

        _serviceProvider = services.BuildServiceProvider();

        // Seed DB
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();

            // Seed admin role + user
            var adminRole = new Role
            {
                Id = Guid.NewGuid(),
                Name = "Admin",
                RolePermissions = [new RolePermission { Permission = "asset:*" }]
            };
            db.Roles.Add(adminRole);

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

            // Generate token
            var authHandler = new AuthHandler(
                _serviceProvider,
                NullLogger<AuthHandler>.Instance,
                config);
            _validToken = authHandler.GenerateTokenForUser(adminUser);

            // Seed a test image asset
            _assetId = Guid.NewGuid();
            var tempFile = Path.Combine(Path.GetTempPath(), $"{_assetId}.jpg");
            await File.WriteAllBytesAsync(tempFile, [0xFF, 0xD8, 0xFF, 0xE0]);

            db.DigitalAssets.Add(new DigitalAsset
            {
                Id = _assetId,
                FileName = $"{_assetId}.jpg",
                FileExtension = ".jpg",
                MimeType = "image/jpeg",
                FileSize = 42,
                ChecksumSha256 = new string('a', 64),
                StoragePath = tempFile.Replace('\\', '/'),
                OriginalPath = tempFile.Replace('\\', '/'),
                Title = "Test Image",
                Type = AssetType.Image,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            });

            // Seed a non-image asset
            var docId = Guid.NewGuid();
            db.DigitalAssets.Add(new DigitalAsset
            {
                Id = docId,
                FileName = "doc.pdf",
                FileExtension = ".pdf",
                MimeType = "application/pdf",
                FileSize = 500,
                ChecksumSha256 = new string('b', 64),
                StoragePath = "/test/doc.pdf",
                OriginalPath = "/test/doc.pdf",
                Title = "Document",
                Type = AssetType.Document,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();
        }

        _handler = _serviceProvider.GetRequiredService<FaceHandler>();
    }

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        try { File.Delete(_dbPath); } catch { }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task DetectFaces_NonImage_ReturnsError()
    {
        var docId = await GetDocumentAssetId();

        var request = CreateRequest(MessageTypeCode.DetectFacesRequest, new DetectFacesRequest
        {
            AssetId = docId.ToString()
        }, _validToken);

        var response = await _handler.DetectFacesAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.StatusCode.Should().NotBe(0);
        response.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DetectFaces_InvalidAssetId_ReturnsError()
    {
        var request = CreateRequest(MessageTypeCode.DetectFacesRequest, new DetectFacesRequest
        {
            AssetId = "not-a-guid"
        }, _validToken);

        var response = await _handler.DetectFacesAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be((int)ErrorCode.InvalidArgument);
    }

    [Fact]
    public async Task DetectFaces_WithoutAuth_ReturnsForbidden()
    {
        var request = CreateRequest(MessageTypeCode.DetectFacesRequest, new DetectFacesRequest
        {
            AssetId = _assetId.ToString()
        }, "");

        var response = await _handler.DetectFacesAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(7); // Forbidden
    }

    [Fact]
    public async Task DetectFaces_MalformedPayload_ReturnsError()
    {
        var request = new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _validToken,
            MessageType = MessageTypeCode.DetectFacesRequest,
            Payload = ByteString.CopyFrom(new byte[] { 0xFF, 0xFE })
        };

        var response = await _handler.DetectFacesAsync(request, CancellationToken.None);

        response.StatusCode.Should().NotBe(0);
    }

    private async Task<Guid> GetDocumentAssetId()
    {
        await using var db = new TestDbContextFactory(_dbPath).CreateDbContext();
        var doc = await db.DigitalAssets.FirstAsync(a => a.Type == AssetType.Document);
        return doc.Id;
    }

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
