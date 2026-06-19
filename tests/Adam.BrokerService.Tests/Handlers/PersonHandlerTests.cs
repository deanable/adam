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
/// Tests for PersonHandler covering list, name, merge, and delete operations.
/// Uses DI with SQLite and seeded data for isolated per-test databases.
/// </summary>
public sealed class PersonHandlerTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private ServiceProvider _serviceProvider = null!;
    private PersonHandler _handler = null!;
    private string _validToken = null!;
    private Guid _aliceId;
    private Guid _bobId;
    private Guid _faceId;

    public PersonHandlerTests()
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
        services.AddSingleton<FaceMatcherService>();
        services.AddSingleton<PersonHandler>();

        _serviceProvider = services.BuildServiceProvider();

        // Seed DB
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();

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

            var authHandler = new AuthHandler(
                _serviceProvider,
                NullLogger<AuthHandler>.Instance,
                config);
            _validToken = authHandler.GenerateTokenForUser(adminUser);

            // Seed persons
            _aliceId = Guid.NewGuid();
            db.Persons.Add(new Person
            {
                Id = _aliceId,
                Name = "Alice Johnson",
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            });

            _bobId = Guid.NewGuid();
            db.Persons.Add(new Person
            {
                Id = _bobId,
                Name = "Bob Smith",
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();

            // Seed a face linked to Alice
            var assetId = Guid.NewGuid();
            db.DigitalAssets.Add(new DigitalAsset
            {
                Id = assetId,
                FileName = "face.jpg",
                FileExtension = ".jpg",
                MimeType = "image/jpeg",
                FileSize = 100,
                ChecksumSha256 = new string('a', 64),
                StoragePath = "/test/face.jpg",
                OriginalPath = "/test/face.jpg",
                Title = "Face",
                Type = AssetType.Image,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();

            _faceId = Guid.NewGuid();
            db.AssetFaces.Add(new AssetFace
            {
                Id = _faceId,
                AssetId = assetId,
                PersonId = _aliceId,
                FaceEmbedding = new byte[2048],
                BoundingBoxJson = "{}",
                DetectionConfidence = 0.95f,
                MatchingConfidence = 0.92f,
                IsAutoAssigned = true
            });
            await db.SaveChangesAsync();
        }

        _handler = _serviceProvider.GetRequiredService<PersonHandler>();
    }

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        try { File.Delete(_dbPath); } catch { }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task ListPersons_ReturnsAll()
    {
        var request = CreateRequest(MessageTypeCode.ListPersonsRequest, new ListPersonsRequest(), _validToken);

        var response = await _handler.ListPersonsAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.StatusCode.Should().Be(0);

        var payload = ProtoHelper.Deserialize<ListPersonsResponse>(response.Payload.ToByteArray());
        payload.Persons.Should().HaveCount(2);
    }

    [Fact]
    public async Task NamePerson_CreatesNewPerson()
    {
        var request = CreateRequest(MessageTypeCode.NamePersonRequest, new NamePersonRequest
        {
            AssetFaceId = _faceId.ToString(),
            PersonName = "Charlie"
        }, _validToken);

        var response = await _handler.NamePersonAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.StatusCode.Should().Be(0);

        var payload = ProtoHelper.Deserialize<NamePersonResponse>(response.Payload.ToByteArray());
        payload.PersonId.Should().NotBeNullOrEmpty();
        Guid.TryParse(payload.PersonId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task NamePerson_LinksToExisting()
    {
        var request = CreateRequest(MessageTypeCode.NamePersonRequest, new NamePersonRequest
        {
            AssetFaceId = _faceId.ToString(),
            PersonName = "Alice Johnson" // existing person
        }, _validToken);

        var response = await _handler.NamePersonAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(0);

        var payload = ProtoHelper.Deserialize<NamePersonResponse>(response.Payload.ToByteArray());
        payload.PersonId.Should().Be(_aliceId.ToString());
    }

    [Fact]
    public async Task MergePersons_CombinesFaces()
    {
        var request = CreateRequest(MessageTypeCode.MergePersonsRequest, new MergePersonsRequest
        {
            SourcePersonId = _aliceId.ToString(),
            TargetPersonId = _bobId.ToString()
        }, _validToken);

        var response = await _handler.MergePersonsAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(0);

        // Verify faces moved to Bob
        await using var db = new TestDbContextFactory(_dbPath).CreateDbContext();
        var face = await db.AssetFaces.FindAsync(_faceId);
        face!.PersonId.Should().Be(_bobId);

        var alice = await db.Persons.FindAsync(_aliceId);
        alice.Should().BeNull("source person should be deleted after merge");
    }

    [Fact]
    public async Task DeletePerson_RemovesAndUnlinks()
    {
        var request = CreateRequest(MessageTypeCode.DeletePersonRequest, new DeletePersonRequest
        {
            PersonId = _aliceId.ToString()
        }, _validToken);

        var response = await _handler.DeletePersonAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(0);

        await using var db = new TestDbContextFactory(_dbPath).CreateDbContext();
        var alice = await db.Persons.FindAsync(_aliceId);
        alice.Should().BeNull("person should be deleted");

        var face = await db.AssetFaces.FindAsync(_faceId);
        face!.PersonId.Should().BeNull("face should be unlinked");
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
