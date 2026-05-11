using System.Net;
using System.Net.Sockets;
using Adam.BrokerService.Handlers;
using Adam.BrokerService.Transport;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Transport;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Adam.BrokerService.Tests.Integration;

public sealed class ConcurrentClientsTests : IAsyncLifetime
{
    private ServiceProvider _serviceProvider = null!;
    private TcpListenerService _listener = null!;
    private int _port;

    public async Task InitializeAsync()
    {
        _port = GetFreePort();

        var dbPath = Path.GetTempFileName();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlite($"Data Source={dbPath}"));

        services.AddSingleton<IConnectionHandler, ConnectionHandler>();
        services.AddSingleton<AuthHandler>();
        services.AddSingleton<AssetHandler>();
        services.AddSingleton<CollectionHandler>();
        services.AddSingleton<ChangeHandler>();
        services.AddSingleton<UserHandler>();
        services.AddSingleton<AuditLogHandler>();
        services.AddSingleton<AuditLogger>();
        services.AddSingleton<StatusHandler>();
        services.AddSingleton<TcpListenerService>();

        _serviceProvider = services.BuildServiceProvider();

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();

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
        }

        _listener = _serviceProvider.GetRequiredService<TcpListenerService>();
        _ = _listener.StartAsync(_port);
        await Task.Delay(500);
    }

    public async Task DisposeAsync()
    {
        _listener.Stop();
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
                    MessageType = nameof(ListAssetsRequest),
                    Payload = ByteString.CopyFrom(
                        ProtoHelper.Serialize(new ListAssetsRequest { Page = 1, PageSize = 50 }))
                };

                await TcpFrame.SendAsync(stream, request);
                var response = await TcpFrame.ReceiveAsync(stream);

                response.Should().NotBeNull();
                response!.StatusCode.Should().Be(0);
                response.MessageType.Should().Be(nameof(ListAssetsResponse));

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
                    MessageType = nameof(ListAssetsRequest),
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
            MessageType = nameof(ListAssetsRequest),
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
            MessageType = nameof(UpdateAssetRequest),
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
            MessageType = nameof(UpdateAssetRequest),
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

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
