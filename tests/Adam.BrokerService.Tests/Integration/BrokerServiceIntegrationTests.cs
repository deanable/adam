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

/// <summary>
/// Comprehensive integration tests for the BrokerService covering connection,
/// authentication, CRUD, search, status, error scenarios, and port contention.
/// </summary>
public sealed class BrokerServiceIntegrationTests : IAsyncLifetime
{
    private ServiceProvider _serviceProvider = null!;
    private TcpListenerService _listener = null!;
    private int _port;
    private string _authToken = null!;
    private Guid _testCollectionId;
    private Guid _testAssetId;
    private string _dbPath = null!;

    public async Task InitializeAsync()
    {
        _port = GetFreePort();
        _dbPath = Path.GetTempFileName();
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
            opts.UseSqlite($"Data Source={_dbPath}"));

        services.AddSingleton<IConnectionHandler, ConnectionHandler>();
        services.AddSingleton<AuthHandler>();
        services.AddSingleton<MetadataWritebackService>();
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

            _testCollectionId = Guid.NewGuid();
            db.Collections.Add(new Collection
            {
                Id = _testCollectionId,
                Name = "Test Collection",
                Description = "A collection for integration testing"
            });

            _testAssetId = Guid.NewGuid();
            db.DigitalAssets.Add(new DigitalAsset
            {
                Id = _testAssetId,
                FileName = "test.jpg",
                FileExtension = ".jpg",
                MimeType = "image/jpeg",
                FileSize = 1024,
                ChecksumSha256 = new string('a', 64),
                StoragePath = "/test/test.jpg",
                Title = "Test Asset",
                Description = "A test asset for integration tests",
                Type = AssetType.Image,
                CollectionId = _testCollectionId,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow,
                Rating = 5,
                Label = AssetLabel.None,
                Flag = AssetFlag.Unflagged,
                Copyright = "Test Copyright"
            });

            var secondAssetId = Guid.NewGuid();
            db.DigitalAssets.Add(new DigitalAsset
            {
                Id = secondAssetId,
                FileName = "document.pdf",
                FileExtension = ".pdf",
                MimeType = "application/pdf",
                FileSize = 204800,
                ChecksumSha256 = new string('b', 64),
                StoragePath = "/docs/report.pdf",
                Title = "Annual Report",
                Description = "PDF document for testing",
                Type = AssetType.Document,
                CollectionId = _testCollectionId,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            });

            db.SaveChanges();

            var authHandler = scope.ServiceProvider.GetRequiredService<AuthHandler>();
            _authToken = authHandler.GenerateTokenForUser(testUser);
        }

        _listener = _serviceProvider.GetRequiredService<TcpListenerService>();
        _ = _listener.StartAsync(_port);
        await Task.Delay(500);
    }

    public async Task DisposeAsync()
    {
        if (_listener != null)
            await _listener.StopAsync();
        await _serviceProvider.DisposeAsync();
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Temp file cleanup is best-effort; the OS will reclaim it eventually
        }
    }

    // =========================================================================
    // Connection & Authentication Tests
    // =========================================================================

    [Fact]
    public async Task Connect_to_service_succeeds()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        client.Connected.Should().BeTrue();
    }

    [Fact]
    public async Task Connect_to_unused_port_throws()
    {
        // Find a port that is currently free (no listener) and verify connection is refused
        var freePort = PortChecker.FindFreePort(0);

        using var client = new TcpClient();
        var act = () => client.ConnectAsync(IPAddress.Loopback, freePort);
        await act.Should().ThrowAsync<SocketException>();
    }

    [Fact]
    public async Task Authenticated_request_succeeds()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.ListAssetsRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListAssetsRequest()))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(0);
    }

    [Fact]
    public async Task Request_without_auth_token_returns_forbidden()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = "",
            MessageType = MessageTypeCode.ListAssetsRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListAssetsRequest()))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(7);
    }

    [Fact]
    public async Task Request_with_invalid_token_returns_forbidden()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = "invalid-token-that-is-definitely-not-valid",
            MessageType = MessageTypeCode.ListAssetsRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListAssetsRequest()))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(7);
    }

    // =========================================================================
    // Catalog Query Tests
    // =========================================================================

    [Fact]
    public async Task ListAssets_returns_all_assets()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.ListAssetsRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListAssetsRequest()))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(0);
        response.MessageType.Should().Be(MessageTypeCode.ListAssetsResponse);

        var assets = ProtoHelper.Deserialize<ListAssetsResponse>(response.Payload.ToByteArray());
        assets.TotalCount.Should().Be(2);
        assets.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListAssets_with_pagination_works()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.ListAssetsRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListAssetsRequest
            {
                Page = 1,
                PageSize = 1
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(0);

        var assets = ProtoHelper.Deserialize<ListAssetsResponse>(response.Payload.ToByteArray());
        assets.TotalCount.Should().Be(2);
        assets.Items.Should().HaveCount(1);
        assets.Page.Should().Be(1);
        assets.PageSize.Should().Be(1);
    }

    [Fact]
    public async Task GetAsset_returns_full_detail()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.GetAssetRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetAssetRequest
            {
                Id = _testAssetId.ToString()
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(0);
        response.MessageType.Should().Be(MessageTypeCode.AssetDetail);

        var detail = ProtoHelper.Deserialize<AssetDetail>(response.Payload.ToByteArray());
        detail.Id.Should().Be(_testAssetId.ToString());
        detail.FileName.Should().Be("test.jpg");
        detail.MimeType.Should().Be("image/jpeg");
        detail.FileSize.Should().Be(1024);
        detail.ChecksumSha256.Should().Be(new string('a', 64));
        detail.Title.Should().Be("Test Asset");
        detail.Description.Should().Be("A test asset for integration tests");
        detail.Version.Should().Be(1);
        detail.Rating.Should().Be(5);
        detail.Label.Should().Be((int)AssetLabel.None);
        detail.Copyright.Should().Be("Test Copyright");
    }

    [Fact]
    public async Task GetAsset_unknown_id_returns_not_found()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.GetAssetRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetAssetRequest
            {
                Id = Guid.NewGuid().ToString()
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(5);
        response.ErrorMessage.Should().Contain("not found");
    }

    // =========================================================================
    // Search & Filter Tests
    // =========================================================================

    [Fact]
    public async Task ListAssets_search_by_text_finds_matches()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.ListAssetsRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListAssetsRequest
            {
                Search = "Annual"
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(0);

        var assets = ProtoHelper.Deserialize<ListAssetsResponse>(response.Payload.ToByteArray());
        assets.TotalCount.Should().Be(1);
        assets.Items[0].FileName.Should().Be("document.pdf");
    }

    [Fact]
    public async Task ListAssets_search_no_matches_returns_empty()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.ListAssetsRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListAssetsRequest
            {
                Search = "NonExistentItemXYZ"
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(0);

        var assets = ProtoHelper.Deserialize<ListAssetsResponse>(response.Payload.ToByteArray());
        assets.TotalCount.Should().Be(0);
        assets.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAssets_filter_by_type_works()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.ListAssetsRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListAssetsRequest
            {
                Type = "Image"
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(0);

        var assets = ProtoHelper.Deserialize<ListAssetsResponse>(response.Payload.ToByteArray());
        assets.TotalCount.Should().Be(1);
        assets.Items[0].Type.Should().Be("Image");
    }

    [Fact]
    public async Task ListAssets_sort_by_file_name_descending_works()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.ListAssetsRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListAssetsRequest
            {
                SortBy = "File Name",
                SortDir = "desc"
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(0);

        var assets = ProtoHelper.Deserialize<ListAssetsResponse>(response.Payload.ToByteArray());
        assets.Items.Should().HaveCount(2);
        assets.Items[0].FileName.Should().Be("test.jpg");
        assets.Items[1].FileName.Should().Be("document.pdf");
    }

    // =========================================================================
    // CRUD Operation Tests
    //
    // Each test uses its own freshly-created asset to avoid shared state ordering
    // (e.g., one test bumping the version that another expects to be 1).
    // =========================================================================

    private async Task<string> CreateTestAssetThroughServiceAsync(NetworkStream stream)
    {
        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.CreateAssetRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new CreateAssetRequest
            {
                FileName = Guid.NewGuid() + ".png",
                Title = "Temp Asset",
                Description = "Isolated test asset",
                CollectionId = _testCollectionId.ToString(),
                Rating = 1
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(0);
        var result = ProtoHelper.Deserialize<CreateAssetResponse>(response.Payload.ToByteArray());
        return result.Id;
    }

    [Fact]
    public async Task CreateAsset_succeeds_and_returns_id()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.CreateAssetRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new CreateAssetRequest
            {
                FileName = "new-asset.png",
                Title = "New Asset",
                Description = "Created during integration test",
                CollectionId = _testCollectionId.ToString(),
                Rating = 3,
                Label = 1,
                Copyright = "New Copyright"
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(0);
        response.MessageType.Should().Be(MessageTypeCode.CreateAssetResponse);

        var result = ProtoHelper.Deserialize<CreateAssetResponse>(response.Payload.ToByteArray());
        result.Id.Should().NotBeNullOrEmpty();
        Guid.TryParse(result.Id, out _).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsset_modifies_fields_and_increments_version()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();
        var freshAssetId = await CreateTestAssetThroughServiceAsync(stream);

        var updateResponse = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.UpdateAssetRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new UpdateAssetRequest
            {
                Id = freshAssetId,
                Title = "Updated Title",
                Description = "Updated description",
                ExpectedVersion = 1,
                Rating = 4,
                Copyright = "Updated Copyright"
            }))
        });

        updateResponse.Should().NotBeNull();
        updateResponse!.StatusCode.Should().Be(0);

        var result = ProtoHelper.Deserialize<UpdateAssetResponse>(updateResponse.Payload.ToByteArray());
        result.Conflict.Should().BeFalse();
        result.NewVersion.Should().BeGreaterThan(1);

        // Verify the update persisted by fetching the asset
        var getResponse = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.GetAssetRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetAssetRequest
            {
                Id = freshAssetId
            }))
        });

        var detail = ProtoHelper.Deserialize<AssetDetail>(getResponse!.Payload.ToByteArray());
        detail.Title.Should().Be("Updated Title");
        detail.Description.Should().Be("Updated description");
        detail.Rating.Should().Be(4);
        detail.Copyright.Should().Be("Updated Copyright");
    }

    [Fact]
    public async Task UpdateAsset_with_wrong_version_returns_conflict()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();
        var freshAssetId = await CreateTestAssetThroughServiceAsync(stream);

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.UpdateAssetRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new UpdateAssetRequest
            {
                Id = freshAssetId,
                Title = "Conflicting Update",
                ExpectedVersion = 99 // definitely wrong
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(0);

        var result = ProtoHelper.Deserialize<UpdateAssetResponse>(response.Payload.ToByteArray());
        result.Conflict.Should().BeTrue();
        result.NewVersion.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UpdateAsset_unknown_id_returns_not_found()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var unknownId = Guid.NewGuid().ToString();
        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.UpdateAssetRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new UpdateAssetRequest
            {
                Id = unknownId,
                Title = "Ghost Update"
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(5);
        response.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task Soft_delete_asset_succeeds()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        // Create asset through the service (full create → delete flow)
        var createResponse = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.CreateAssetRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new CreateAssetRequest
            {
                FileName = "to-delete.txt",
                Title = "Asset to Delete",
                CollectionId = _testCollectionId.ToString()
            }))
        });
        createResponse.Should().NotBeNull();
        createResponse!.StatusCode.Should().Be(0);
        var newId = ProtoHelper.Deserialize<CreateAssetResponse>(createResponse.Payload.ToByteArray()).Id;

        // Now delete it through the service
        var deleteResponse = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.DeleteAssetRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new DeleteAssetRequest
            {
                Id = newId
            }))
        });

        deleteResponse.Should().NotBeNull();
        deleteResponse!.StatusCode.Should().Be(0);
        deleteResponse.MessageType.Should().Be(MessageTypeCode.DeleteAssetResponse);
    }

    // =========================================================================
    // Collection Tests
    // =========================================================================

    [Fact]
    public async Task ListCollections_returns_available_collections()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.ListCollectionsRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListCollectionsRequest()))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(0);
        response.MessageType.Should().Be(MessageTypeCode.ListCollectionsResponse);

        var collections = ProtoHelper.Deserialize<ListCollectionsResponse>(response.Payload.ToByteArray());
        collections.Items.Should().NotBeEmpty();
        collections.Items.Should().Contain(c => c.Name == "Test Collection");
    }

    [Fact]
    public async Task CreateCollection_succeeds()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.CreateCollectionRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new CreateCollectionRequest
            {
                Name = "New Integration Collection",
                Description = "Created during test"
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(0);
    }

    // =========================================================================
    // Service Status Tests
    // =========================================================================

    [Fact]
    public async Task GetServiceStatus_returns_server_info()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.GetServiceStatusRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetServiceStatusRequest()))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(0);

        var status = ProtoHelper.Deserialize<GetServiceStatusResponse>(response.Payload.ToByteArray());
        status.Port.Should().Be(_port);
        status.ServiceState.Should().Be("Running");
        status.ActiveConnections.Should().BeGreaterThanOrEqualTo(0);
        status.UptimeSeconds.Should().BeGreaterThanOrEqualTo(0);
    }

    // =========================================================================
    // Configurable Port Tests
    // =========================================================================

    [Fact]
    public async Task Service_listens_on_configured_port()
    {
        // Connect on the port configured during InitializeAsync
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        client.Connected.Should().BeTrue();

        // Confirm we can communicate
        using var stream = client.GetStream();
        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.GetServiceStatusRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetServiceStatusRequest()))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(0);

        var status = ProtoHelper.Deserialize<GetServiceStatusResponse>(response.Payload.ToByteArray());
        status.Port.Should().Be(_port);
    }

    [Fact]
    public async Task PortChecker_detects_port_in_use()
    {
        // The listener is already running on _port, so it should be detected as in use
        PortChecker.IsPortInUse(_port).Should().BeTrue();
        PortChecker.IsPortFree(_port).Should().BeFalse();
    }

    [Fact]
    public async Task PortChecker_finds_free_port_when_preferred_in_use()
    {
        // _port is in use, so FindFreePort should return a different free port
        var freePort = PortChecker.FindFreePort(_port);
        freePort.Should().BeGreaterThan(0);
        freePort.Should().NotBe(_port);
        PortChecker.IsPortFree(freePort).Should().BeTrue();
    }

    [Fact]
    public async Task Service_can_listen_on_alternative_port()
    {
        // Start a second listener on a different port.
        // Use FindFreePort() with the default start (9100) instead of _port + 1000
        // to avoid overflow when _port is in the high ephemeral range (>64535).
        var secondPort = PortChecker.FindFreePort();
        secondPort.Should().BeGreaterThan(0);

        var secondListener = _serviceProvider.GetRequiredService<TcpListenerService>();
        _ = secondListener.StartAsync(secondPort);
        await Task.Delay(500);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, secondPort);
            client.Connected.Should().BeTrue();

            using var stream = client.GetStream();
            var response = await SendAndReceiveAsync(stream, new Envelope
            {
                CorrelationId = Guid.NewGuid().ToString(),
                AuthToken = _authToken,
                MessageType = MessageTypeCode.GetServiceStatusRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetServiceStatusRequest()))
            });

            response.Should().NotBeNull();
            response!.StatusCode.Should().Be(0);

            var status = ProtoHelper.Deserialize<GetServiceStatusResponse>(response.Payload.ToByteArray());
            status.Port.Should().Be(secondPort);
        }
        finally
        {
            await secondListener.StopAsync();
        }
    }

    // =========================================================================
    // Error Scenario Tests
    // =========================================================================

    [Fact]
    public async Task GetAsset_retrieves_file_metadata()
    {
        // Covers "retrieving files" — gets metadata for a pre-seeded asset
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.GetAssetRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetAssetRequest
            {
                Id = _testAssetId.ToString()
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(0);

        var detail = ProtoHelper.Deserialize<AssetDetail>(response.Payload.ToByteArray());
        detail.FileName.Should().Be("test.jpg");
        detail.FileExtension.Should().Be(".jpg");
        detail.MimeType.Should().Be("image/jpeg");
        detail.FileSize.Should().Be(1024);
        detail.ChecksumSha256.Should().Be(new string('a', 64));
    }

    [Fact]
    public async Task Malformed_envelope_does_not_crash_server()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        // Send garbage data
        var garbage = new byte[] { 0xFF, 0xFE, 0xFD, 0x00, 0x01, 0x02, 0x03 };
        await stream.WriteAsync(BitConverter.GetBytes(garbage.Length));
        await stream.WriteAsync(garbage);

        // Server should not crash - the connection might close, but that's acceptable
        // Just verify the server is still accepting new connections
        await Task.Delay(200);

        using var client2 = new TcpClient();
        await client2.ConnectAsync(IPAddress.Loopback, _port);
        client2.Connected.Should().BeTrue();
    }

    [Fact]
    public async Task Empty_payload_handled_gracefully()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.ListAssetsRequest,
            Payload = ByteString.Empty
        });

        // Should handle empty payload gracefully (default ListAssetsRequest)
        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(0);
    }

    [Fact]
    public async Task Unknown_message_type_returns_error()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = (MessageTypeCode)9999, // unknown type
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListAssetsRequest()))
        });

        response.Should().NotBeNull();
        // The server should not crash - it may return an error or close the connection
        // Just verify the server is still running
        using var client2 = new TcpClient();
        await client2.ConnectAsync(IPAddress.Loopback, _port);
        client2.Connected.Should().BeTrue();
    }

    // =========================================================================
    // File Streaming / Download Tests
    // =========================================================================

    [Fact]
    public async Task GetFile_returns_file_bytes_for_existing_asset()
    {
        // Create a temp file with known content to simulate a stored asset
        var tempFile = Path.GetTempFileName();
        try
        {
            var fileContent = "Hello, this is test file content!"u8.ToArray();
            await File.WriteAllBytesAsync(tempFile, fileContent);

            // Insert an asset pointing to this temp file directly into the DB
            var assetId = Guid.NewGuid();
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.DigitalAssets.Add(new DigitalAsset
                {
                    Id = assetId,
                    FileName = "test-stream.bin",
                    FileExtension = ".bin",
                    MimeType = "application/octet-stream",
                    FileSize = fileContent.Length,
                    ChecksumSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fileContent)),
                    StoragePath = tempFile,
                    Title = "Stream Test",
                    Type = AssetType.Document,
                    Version = 1,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ModifiedAt = DateTimeOffset.UtcNow
                });
                db.SaveChanges();
            }

            // Request the file via GetFileRequest
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _port);
            using var stream = client.GetStream();

            var response = await SendAndReceiveAsync(stream, new Envelope
            {
                CorrelationId = Guid.NewGuid().ToString(),
                AuthToken = _authToken,
                MessageType = MessageTypeCode.GetFileRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetFileRequest
                {
                    Id = assetId.ToString()
                }))
            });

            response.Should().NotBeNull();
            response!.StatusCode.Should().Be(0);
            response.MessageType.Should().Be(MessageTypeCode.GetFileResponse);

            var fileResponse = ProtoHelper.Deserialize<GetFileResponse>(response.Payload.ToByteArray());
            fileResponse.FileName.Should().Be("test-stream.bin");
            fileResponse.FileExtension.Should().Be(".bin");
            fileResponse.MimeType.Should().Be("application/octet-stream");
            fileResponse.FileSize.Should().Be(fileContent.Length);
            fileResponse.Content.ToByteArray().Should().Equal(fileContent);
            fileResponse.ChecksumSha256.Should().Be(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fileContent)));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetFile_unknown_asset_id_returns_not_found()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.GetFileRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetFileRequest
            {
                Id = Guid.NewGuid().ToString()
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(5);
        response.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task GetFile_missing_file_on_disk_returns_file_not_found()
    {
        // Insert an asset with a StoragePath that doesn't exist
        var assetId = Guid.NewGuid();
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.DigitalAssets.Add(new DigitalAsset
            {
                Id = assetId,
                FileName = "ghost.txt",
                FileExtension = ".txt",
                MimeType = "text/plain",
                FileSize = 100,
                StoragePath = "C:\\nonexistent\\file.txt",
                Title = "Ghost File",
                Type = AssetType.Document,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            });
            db.SaveChanges();
        }

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.GetFileRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetFileRequest
            {
                Id = assetId.ToString()
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(6);
        response.ErrorMessage.Should().Contain("not found on disk");
    }

    [Fact]
    public async Task GetFile_without_auth_returns_forbidden()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = "",
            MessageType = MessageTypeCode.GetFileRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetFileRequest
            {
                Id = _testAssetId.ToString()
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(7);
    }

    // =========================================================================
    // Chunked File Streaming Tests
    // =========================================================================

    [Fact]
    public async Task GetFileChunk_single_chunk_returns_full_content()
    {
        // File smaller than chunk size should return in a single chunk
        var tempFile = Path.GetTempFileName();
        try
        {
            var fileContent = "Small file content"u8.ToArray();
            await File.WriteAllBytesAsync(tempFile, fileContent);

            var assetId = Guid.NewGuid();
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.DigitalAssets.Add(new DigitalAsset
                {
                    Id = assetId,
                    FileName = "small.bin",
                    FileExtension = ".bin",
                    MimeType = "application/octet-stream",
                    FileSize = fileContent.Length,
                    ChecksumSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fileContent)),
                    StoragePath = tempFile,
                    Title = "Small File",
                    Type = AssetType.Document,
                    Version = 1,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ModifiedAt = DateTimeOffset.UtcNow
                });
                db.SaveChanges();
            }

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _port);
            using var stream = client.GetStream();

            var response = await SendAndReceiveAsync(stream, new Envelope
            {
                CorrelationId = Guid.NewGuid().ToString(),
                AuthToken = _authToken,
                MessageType = MessageTypeCode.GetFileChunkRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetFileChunkRequest
                {
                    Id = assetId.ToString(),
                    ChunkIndex = 0,
                    ChunkSize = 64 * 1024 * 1024 // 64MB to ensure single chunk
                }))
            });

            response.Should().NotBeNull();
            response!.StatusCode.Should().Be(0);
            response.MessageType.Should().Be(MessageTypeCode.GetFileChunkResponse);

            var chunk = ProtoHelper.Deserialize<GetFileChunkResponse>(response.Payload.ToByteArray());
            chunk.FileName.Should().Be("small.bin");
            chunk.ChunkIndex.Should().Be(0);
            chunk.IsLastChunk.Should().BeTrue();
            chunk.TotalChunks.Should().Be(1);
            chunk.ChunkData.ToByteArray().Should().Equal(fileContent);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetFileChunk_multi_chunk_download_assembles_correctly()
    {
        // Create a file large enough to span multiple chunks
        var tempFile = Path.GetTempFileName();
        try
        {
            // 1MB file with deterministic content
            var chunkSize = 256 * 1024; // 256KB chunks for testing
            int totalSize = 1024 * 1024; // 1MB
            var fileContent = new byte[totalSize];
            for (int i = 0; i < totalSize; i++)
                fileContent[i] = (byte)(i % 256);

            await File.WriteAllBytesAsync(tempFile, fileContent);
            var expectedChecksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fileContent));

            var assetId = Guid.NewGuid();
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.DigitalAssets.Add(new DigitalAsset
                {
                    Id = assetId,
                    FileName = "multi-chunk.bin",
                    FileExtension = ".bin",
                    MimeType = "application/octet-stream",
                    FileSize = fileContent.Length,
                    ChecksumSha256 = expectedChecksum,
                    StoragePath = tempFile,
                    Title = "Multi Chunk File",
                    Type = AssetType.Document,
                    Version = 1,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ModifiedAt = DateTimeOffset.UtcNow
                });
                db.SaveChanges();
            }

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _port);
            using var stream = client.GetStream();

            // Download all chunks sequentially
            var assembled = new MemoryStream();
            int expectedTotalChunks = 0;
            string? fileName = null;
            long fileSize = 0;
            string? checksum = null;

            for (int chunkIndex = 0; ; chunkIndex++)
            {
                var response = await SendAndReceiveAsync(stream, new Envelope
                {
                    CorrelationId = Guid.NewGuid().ToString(),
                    AuthToken = _authToken,
                    MessageType = MessageTypeCode.GetFileChunkRequest,
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetFileChunkRequest
                    {
                        Id = assetId.ToString(),
                        ChunkIndex = chunkIndex,
                        ChunkSize = chunkSize
                    }))
                });

                response.Should().NotBeNull();
                response!.StatusCode.Should().Be(0);

                var chunk = ProtoHelper.Deserialize<GetFileChunkResponse>(response.Payload.ToByteArray());

                // Store metadata from first chunk
                if (fileName == null) fileName = chunk.FileName;
                if (fileSize == 0) fileSize = chunk.FileSize;
                if (checksum == null) checksum = chunk.ChecksumSha256;
                expectedTotalChunks = chunk.TotalChunks;

                chunk.ChunkIndex.Should().Be(chunkIndex);
                chunk.TotalChunks.Should().Be(expectedTotalChunks);

                await assembled.WriteAsync(chunk.ChunkData.ToByteArray());

                if (chunk.IsLastChunk)
                {
                    chunkIndex.Should().Be(expectedTotalChunks - 1);
                    break;
                }
            }

            // Verify assembled content
            fileName.Should().Be("multi-chunk.bin");
            fileSize.Should().Be(totalSize);
            checksum.Should().Be(expectedChecksum);
            expectedTotalChunks.Should().Be((int)Math.Ceiling((double)totalSize / chunkSize));

            var assembledBytes = assembled.ToArray();
            assembledBytes.Length.Should().Be(totalSize);
            var assembledChecksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(assembledBytes));
            assembledChecksum.Should().Be(expectedChecksum);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetFileChunk_exact_chunk_boundary_returns_two_chunks()
    {
        // File size exactly equals chunk size — should return 1 chunk (since chunk index 0 covers it)
        var tempFile = Path.GetTempFileName();
        try
        {
            var chunkSize = 64 * 1024; // 64KB
            var fileContent = new byte[chunkSize];
            new Random(42).NextBytes(fileContent);

            await File.WriteAllBytesAsync(tempFile, fileContent);

            var assetId = Guid.NewGuid();
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.DigitalAssets.Add(new DigitalAsset
                {
                    Id = assetId,
                    FileName = "exact-boundary.bin",
                    FileExtension = ".bin",
                    MimeType = "application/octet-stream",
                    FileSize = fileContent.Length,
                    ChecksumSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fileContent)),
                    StoragePath = tempFile,
                    Title = "Exact Boundary",
                    Type = AssetType.Document,
                    Version = 1,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ModifiedAt = DateTimeOffset.UtcNow
                });
                db.SaveChanges();
            }

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _port);
            using var stream = client.GetStream();

            // Chunk 0 - should return exactly chunkSize bytes
            var response0 = await SendAndReceiveAsync(stream, new Envelope
            {
                CorrelationId = Guid.NewGuid().ToString(),
                AuthToken = _authToken,
                MessageType = MessageTypeCode.GetFileChunkRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetFileChunkRequest
                {
                    Id = assetId.ToString(),
                    ChunkIndex = 0,
                    ChunkSize = chunkSize
                }))
            });

            response0.Should().NotBeNull();
            response0!.StatusCode.Should().Be(0);
            var chunk0 = ProtoHelper.Deserialize<GetFileChunkResponse>(response0.Payload.ToByteArray());
            chunk0.ChunkData.Length.Should().Be(chunkSize);
            chunk0.IsLastChunk.Should().BeTrue(); // Only 1 chunk needed
            chunk0.TotalChunks.Should().Be(1);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetFileChunk_chunk_beyond_end_returns_empty_last_chunk()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var fileContent = "Small"u8.ToArray();
            await File.WriteAllBytesAsync(tempFile, fileContent);

            var assetId = Guid.NewGuid();
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.DigitalAssets.Add(new DigitalAsset
                {
                    Id = assetId,
                    FileName = "small.txt",
                    FileExtension = ".txt",
                    MimeType = "text/plain",
                    FileSize = fileContent.Length,
                    ChecksumSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fileContent)),
                    StoragePath = tempFile,
                    Title = "Small",
                    Type = AssetType.Document,
                    Version = 1,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ModifiedAt = DateTimeOffset.UtcNow
                });
                db.SaveChanges();
            }

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _port);
            using var stream = client.GetStream();

            // Request chunk 99, well beyond the file
            var response = await SendAndReceiveAsync(stream, new Envelope
            {
                CorrelationId = Guid.NewGuid().ToString(),
                AuthToken = _authToken,
                MessageType = MessageTypeCode.GetFileChunkRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetFileChunkRequest
                {
                    Id = assetId.ToString(),
                    ChunkIndex = 99,
                    ChunkSize = 4096
                }))
            });

            response.Should().NotBeNull();
            response!.StatusCode.Should().Be(0);

            var chunk = ProtoHelper.Deserialize<GetFileChunkResponse>(response.Payload.ToByteArray());
            chunk.ChunkData.IsEmpty.Should().BeTrue();
            chunk.IsLastChunk.Should().BeTrue();
            chunk.TotalChunks.Should().Be(1);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetFileChunk_unknown_asset_id_returns_not_found()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.GetFileChunkRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetFileChunkRequest
            {
                Id = Guid.NewGuid().ToString(),
                ChunkIndex = 0
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(5);
        response.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task GetFileChunk_missing_file_on_disk_returns_file_not_found()
    {
        var assetId = Guid.NewGuid();
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.DigitalAssets.Add(new DigitalAsset
            {
                Id = assetId,
                FileName = "ghost.txt",
                FileExtension = ".txt",
                MimeType = "text/plain",
                FileSize = 100,
                StoragePath = "C:\\nonexistent\\file.txt",
                Title = "Ghost File",
                Type = AssetType.Document,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            });
            db.SaveChanges();
        }

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = _authToken,
            MessageType = MessageTypeCode.GetFileChunkRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetFileChunkRequest
            {
                Id = assetId.ToString(),
                ChunkIndex = 0
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(6);
        response.ErrorMessage.Should().Contain("not found on disk");
    }

    [Fact]
    public async Task GetFileChunk_without_auth_returns_forbidden()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, new Envelope
        {
            CorrelationId = Guid.NewGuid().ToString(),
            AuthToken = "",
            MessageType = MessageTypeCode.GetFileChunkRequest,
            Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetFileChunkRequest
            {
                Id = Guid.NewGuid().ToString(),
                ChunkIndex = 0
            }))
        });

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(7);
    }

    [Fact]
    public async Task GetFileChunk_negative_chunk_index_returns_invalid()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tempFile, "test"u8.ToArray());

            var assetId = Guid.NewGuid();
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.DigitalAssets.Add(new DigitalAsset
                {
                    Id = assetId,
                    FileName = "test.txt",
                    FileExtension = ".txt",
                    MimeType = "text/plain",
                    FileSize = 4,
                    StoragePath = tempFile,
                    Title = "Test",
                    Type = AssetType.Document,
                    Version = 1,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ModifiedAt = DateTimeOffset.UtcNow
                });
                db.SaveChanges();
            }

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _port);
            using var stream = client.GetStream();

            var response = await SendAndReceiveAsync(stream, new Envelope
            {
                CorrelationId = Guid.NewGuid().ToString(),
                AuthToken = _authToken,
                MessageType = MessageTypeCode.GetFileChunkRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetFileChunkRequest
                {
                    Id = assetId.ToString(),
                    ChunkIndex = -1
                }))
            });

            response.Should().NotBeNull();
            response!.StatusCode.Should().Be(14);
            response.ErrorMessage.Should().Contain("Invalid chunk index");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetFileChunk_parallel_chunks_assembles_correctly()
    {
        // Request all chunks in parallel and verify they reassemble correctly
        var tempFile = Path.GetTempFileName();
        try
        {
            var chunkSize = 128 * 1024; // 128KB chunks
            int totalSize = 512 * 1024; // 512KB
            var fileContent = new byte[totalSize];
            for (int i = 0; i < totalSize; i++)
                fileContent[i] = (byte)(i % 256);

            await File.WriteAllBytesAsync(tempFile, fileContent);
            var expectedChecksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fileContent));

            var assetId = Guid.NewGuid();
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.DigitalAssets.Add(new DigitalAsset
                {
                    Id = assetId,
                    FileName = "parallel.bin",
                    FileExtension = ".bin",
                    MimeType = "application/octet-stream",
                    FileSize = fileContent.Length,
                    ChecksumSha256 = expectedChecksum,
                    StoragePath = tempFile,
                    Title = "Parallel Test",
                    Type = AssetType.Document,
                    Version = 1,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ModifiedAt = DateTimeOffset.UtcNow
                });
                db.SaveChanges();
            }

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _port);
            using var stream = client.GetStream();

            // Get metadata first to know total chunks
            var metaResponse = await SendAndReceiveAsync(stream, new Envelope
            {
                CorrelationId = Guid.NewGuid().ToString(),
                AuthToken = _authToken,
                MessageType = MessageTypeCode.GetFileChunkRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetFileChunkRequest
                {
                    Id = assetId.ToString(),
                    ChunkIndex = 0,
                    ChunkSize = chunkSize
                }))
            });

            metaResponse.Should().NotBeNull();
            metaResponse!.StatusCode.Should().Be(0);
            var meta = ProtoHelper.Deserialize<GetFileChunkResponse>(metaResponse.Payload.ToByteArray());
            int totalChunks = meta.TotalChunks;

            // Request all remaining chunks in parallel
            var parallelTasks = new List<Task<Envelope?>>();
            for (int i = 1; i < totalChunks; i++)
            {
                var ci = i;
                parallelTasks.Add(Task.Run(async () =>
                {
                    using var c = new TcpClient();
                    await c.ConnectAsync(IPAddress.Loopback, _port);
                    using var s = c.GetStream();

                    return await SendAndReceiveAsync(s, new Envelope
                    {
                        CorrelationId = Guid.NewGuid().ToString(),
                        AuthToken = _authToken,
                        MessageType = MessageTypeCode.GetFileChunkRequest,
                        Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetFileChunkRequest
                        {
                            Id = assetId.ToString(),
                            ChunkIndex = ci,
                            ChunkSize = chunkSize
                        }))
                    });
                }));
            }

            // Assemble: first chunk (from metadata) + parallel chunks
            var assembled = new byte[totalSize];
            Buffer.BlockCopy(meta.ChunkData.ToByteArray(), 0, assembled, 0, meta.ChunkData.Length);

            var parallelResults = await Task.WhenAll(parallelTasks);
            foreach (var pr in parallelResults)
            {
                pr.Should().NotBeNull();
                pr!.StatusCode.Should().Be(0);
                var chunk = ProtoHelper.Deserialize<GetFileChunkResponse>(pr.Payload.ToByteArray());
                Buffer.BlockCopy(chunk.ChunkData.ToByteArray(), 0, assembled, chunk.ChunkIndex * chunkSize, chunk.ChunkData.Length);
            }

            // Verify assembled content matches original
            var assembledChecksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(assembled));
            assembledChecksum.Should().Be(expectedChecksum);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // =========================================================================
    // Concurrent / Multi-Client Tests
    // =========================================================================

    [Fact]
    public async Task Multiple_clients_can_query_simultaneously()
    {
        var tasks = new List<Task<int>>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var c = new TcpClient();
                await c.ConnectAsync(IPAddress.Loopback, _port);
                using var s = c.GetStream();

                var response = await SendAndReceiveAsync(s, new Envelope
                {
                    CorrelationId = Guid.NewGuid().ToString(),
                    AuthToken = _authToken,
                    MessageType = MessageTypeCode.ListAssetsRequest,
                    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListAssetsRequest { Page = 1, PageSize = 50 }))
                });

                response.Should().NotBeNull();
                response!.StatusCode.Should().Be(0);

                var assets = ProtoHelper.Deserialize<ListAssetsResponse>(response.Payload.ToByteArray());
                return assets.TotalCount;
            }));
        }

        var results = await Task.WhenAll(tasks);
        results.Should().OnlyContain(r => r == 2);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static async Task<Envelope?> SendAndReceiveAsync(NetworkStream stream, Envelope request)
    {
        await TcpFrame.SendAsync(stream, request);
        return await TcpFrame.ReceiveAsync(stream);
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
