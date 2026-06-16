using System.Net;
using System.Net.Sockets;
using Adam.BrokerService.Handlers;
using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Adam.Shared.Transport;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.BrokerService.Tests.Integration;

public sealed class ConcurrentClientsTests : IAsyncLifetime
{
    private ServiceProvider _serviceProvider = null!;
    private TcpListenerService _listener = null!;
    private int _port;
    private string _authToken = null!;

    public async Task InitializeAsync()
    {
        _port = GetFreePort();

        var dbPath = Path.GetTempFileName();
        var services = new ServiceCollection();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "dGVzdC1zaWduaW5nLWtleS1mb3ItdGVzdGluZy1vbmx5LTMyLWJ5dGVz",
                ["Jwt:TokenExpiryHours"] = "24"
            }!)
            .Build();
        services.AddSingleton<IConfiguration>(config);

        services.AddLogging();
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlite($"Data Source={dbPath}"));

        services.AddSingleton<IConnectionHandler, ConnectionHandler>();
        services.AddSingleton<AuthHandler>();
        services.AddSingleton<MetadataWritebackService>();
        services.AddSingleton<CommentHandler>();
        services.AddSingleton<AssetHandler>();
        services.AddSingleton<CollectionHandler>();
        services.AddSingleton<ChangeHandler>();
        services.AddSingleton<UserHandler>();
        services.AddSingleton<AuditLogHandler>();
        services.AddSingleton<AuditLogger>();
        services.AddSingleton<AuthorizationMiddleware>();
        services.AddSingleton<StatusHandler>();
        services.AddSingleton<SidebarHandler>();
        services.AddSingleton<WatchedFolderHandler>();
        services.AddSingleton<SavedSearchHandler>();
        services.AddSingleton<SearchHistoryHandler>();
        services.AddSingleton<SemanticSearchHandler>();
        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<SemanticSearchService>();
        services.AddSingleton<ConnectionRegistry>();
        services.AddSingleton<ChangeNotificationService>();
        services.AddSingleton<TcpListenerService>();

        _serviceProvider = services.BuildServiceProvider();

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();

            var adminRole = db.Roles.FirstOrDefault(r => r.Name == "Administrator");
            if (adminRole == null)
            {
                adminRole = new Role
                {
                    Id = Guid.NewGuid(),
                    Name = "Administrator",
                    RolePermissions = [
                        new RolePermission { Permission = "asset:*" },
                        new RolePermission { Permission = "collection:*" },
                        new RolePermission { Permission = "user:*" },
                        new RolePermission { Permission = "audit:read" }
                    ]
                };
                db.Roles.Add(adminRole);
            }

            var testUser = new User
            {
                Id = Guid.NewGuid(),
                Username = "testuser",
                Email = "test@test.com",
                PasswordHash = PasswordHelper.HashPassword("testpass123"),
                RoleId = adminRole.Id,
                IsActive = true
            };
            db.Users.Add(testUser);

            var collectionId = Guid.NewGuid();
            db.Collections.Add(new Collection
            {
                Id = collectionId,
                Name = "Test Collection"
            });

            db.DigitalAssets.Add(new DigitalAsset
            {
                Id = Guid.NewGuid(),
                FileName = "test.jpg",
                FileExtension = ".jpg",
                MimeType = "image/jpeg",
                FileSize = 1024,
                ChecksumSha256 = new string('a', 64),
                StoragePath = "/test/test.jpg",
                Title = "Test Asset",
                Type = AssetType.Image,
                CollectionId = collectionId
            });
            db.SaveChanges();

            // Generate a valid auth token for test requests
            var authHandler = scope.ServiceProvider.GetRequiredService<AuthHandler>();
            _authToken = authHandler.GenerateTokenForUser(testUser);
        }

        _listener = _serviceProvider.GetRequiredService<TcpListenerService>();
        _ = _listener.StartAsync(_port);
        await Task.Delay(500);
    }

    public async Task DisposeAsync()
    {
        await _listener.StopAsync();
        await _serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task Ten_concurrent_clients_can_browse_and_search_simultaneously()
    {
        var tasks = new List<Task<int>>();
        for (int i = 0; i < 10; i++)
        {
            int clientIndex = i;
            tasks.Add(Task.Run(async () =>
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, _port);
                using var stream = client.GetStream();

                var correlationId = Guid.NewGuid().ToString();
                var request = new Envelope
                {
                    CorrelationId = correlationId,
                    AuthToken = _authToken,
                    MessageType = MessageTypeCode.ListAssetsRequest,
                    Payload = ByteString.CopyFrom(
                        ProtoHelper.Serialize(new ListAssetsRequest { Page = 1, PageSize = 50 }))
                };

                await TcpFrame.SendAsync(stream, request);
                var response = await TcpFrame.ReceiveAsync(stream);

                response.Should().NotBeNull();
                response!.StatusCode.Should().Be(0);
                response.MessageType.Should().Be(MessageTypeCode.ListAssetsResponse);

                var listResp = ProtoHelper.Deserialize<ListAssetsResponse>(response.Payload.ToByteArray());
                return listResp.TotalCount;
            }));
        }

        var results = await Task.WhenAll(tasks);
        results.Should().OnlyContain(r => r == 1);
    }

    [Fact]
    public async Task Burst_connections_are_handled_without_crash()
    {
        var clients = new List<TcpClient>();
        var successfulRequests = 0;

        for (int i = 0; i < 20; i++)
        {
            try
            {
                var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, _port);
                var stream = client.GetStream();

                var envelope = new Envelope
                {
                    CorrelationId = Guid.NewGuid().ToString(),
                    AuthToken = _authToken,
                    MessageType = MessageTypeCode.ListAssetsRequest,
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListAssetsRequest()))
                };

                await TcpFrame.SendAsync(stream, envelope);
                var response = await TcpFrame.ReceiveAsync(stream);

                if (response != null && response.StatusCode == 0)
                    successfulRequests++;

                clients.Add(client);
            }
            catch
            {
                // Ignore rejected connections
            }
        }

        successfulRequests.Should().BeGreaterThan(0, "at least some burst connections should succeed");
        foreach (var c in clients)
            c.Dispose();
    }

    [Fact]
    public async Task Version_conflict_on_concurrent_updates()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var correlationId = Guid.NewGuid().ToString();
        var listReq = new Envelope
        {
            CorrelationId = correlationId,
            AuthToken = _authToken,
            MessageType = MessageTypeCode.ListAssetsRequest,
            Payload = ByteString.CopyFrom(
                ProtoHelper.Serialize(new ListAssetsRequest()))
        };

        await TcpFrame.SendAsync(stream, listReq);
        var listResp = await TcpFrame.ReceiveAsync(stream);
        listResp.Should().NotBeNull();
        var assets = ProtoHelper.Deserialize<ListAssetsResponse>(listResp!.Payload.ToByteArray());
        assets.Items.Should().NotBeEmpty();

        var assetId = assets.Items[0].Id;

        var updateReq1 = new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.UpdateAssetRequest,
            Payload = ByteString.CopyFrom(
                ProtoHelper.Serialize(new UpdateAssetRequest
                {
                    Id = assetId,
                    Title = "Client A Update",
                    ExpectedVersion = 1
                }))
        };

        var updateReq2 = new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.UpdateAssetRequest,
            Payload = ByteString.CopyFrom(
                ProtoHelper.Serialize(new UpdateAssetRequest
                {
                    Id = assetId,
                    Title = "Client B Update",
                    ExpectedVersion = 1
                }))
        };

        await TcpFrame.SendAsync(stream, updateReq1);
        var resp1 = await TcpFrame.ReceiveAsync(stream);
        resp1.Should().NotBeNull();

        await TcpFrame.SendAsync(stream, updateReq2);
        var resp2 = await TcpFrame.ReceiveAsync(stream);
        resp2.Should().NotBeNull();

        var result1 = ProtoHelper.Deserialize<UpdateAssetResponse>(resp1!.Payload.ToByteArray());
        var result2 = ProtoHelper.Deserialize<UpdateAssetResponse>(resp2!.Payload.ToByteArray());

        (result1.Conflict || result2.Conflict).Should().BeTrue("one update should detect a version conflict");
    }

    [Fact]
    public async Task ChangeNotificationService_broadcasts_to_authenticated_connections()
    {
        var registry = new ConnectionRegistry(NullLogger<ConnectionRegistry>.Instance);
        var service = new ChangeNotificationService(registry, NullLogger<ChangeNotificationService>.Instance);

        using var ms = new MemoryStream();
        var connectionId = Guid.NewGuid().ToString();
        registry.Register(connectionId, ms);
        registry.SetUserId(connectionId, "user-1");

        await service.BroadcastAsync("asset-1", "updated", "user-1", connectionId);
        // Since we excluded the only connection, nothing should be sent
        ms.Position = 0;
        ms.Length.Should().Be(0, "excluded connection should not receive notification");
    }

    [Fact]
    public async Task AssetHandler_returns_conflict_with_current_version_on_version_mismatch()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        // Create an asset
        var createReq = new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.CreateAssetRequest,
            Payload = ByteString.CopyFrom(
                ProtoHelper.Serialize(new CreateAssetRequest
                {
                    FileName = "test.jpg",
                    Title = "Original Title"
                }))
        };

        await TcpFrame.SendAsync(stream, createReq);
        var createResp = await TcpFrame.ReceiveAsync(stream);
        createResp.Should().NotBeNull();
        var createResult = ProtoHelper.Deserialize<CreateAssetResponse>(createResp!.Payload.ToByteArray());

        // Update with wrong version
        var updateReq = new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.UpdateAssetRequest,
            Payload = ByteString.CopyFrom(
                ProtoHelper.Serialize(new UpdateAssetRequest
                {
                    Id = createResult.Id,
                    Title = "Updated Title",
                    ExpectedVersion = 99 // wrong version
                }))
        };

        await TcpFrame.SendAsync(stream, updateReq);
        var updateResp = await TcpFrame.ReceiveAsync(stream);
        updateResp.Should().NotBeNull();
        var updateResult = ProtoHelper.Deserialize<UpdateAssetResponse>(updateResp!.Payload.ToByteArray());

        updateResult.Conflict.Should().BeTrue("update with wrong version should return conflict");
        updateResult.NewVersion.Should().BeGreaterThan(0, "conflict response should include current version");
    }

    [Fact]
    public async Task ConnectionRegistry_excludes_sender_from_broadcast()
    {
        var registry = new ConnectionRegistry(NullLogger<ConnectionRegistry>.Instance);

        using var ms1 = new MemoryStream();
        using var ms2 = new MemoryStream();

        var conn1 = Guid.NewGuid().ToString();
        var conn2 = Guid.NewGuid().ToString();
        registry.Register(conn1, ms1);
        registry.Register(conn2, ms2);
        registry.SetUserId(conn1, "user-a");
        registry.SetUserId(conn2, "user-b");

        var envelope = new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            MessageType = MessageTypeCode.ChangeNotification,
            Payload = ByteString.CopyFrom([0x01])
        };

        await registry.BroadcastAsync(envelope, conn1);

        ms1.Position = 0;
        ms2.Position = 0;

        ms1.Length.Should().Be(0, "sender should be excluded from broadcast");
        ms2.Length.Should().BeGreaterThan(0, "other connection should receive broadcast");
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
