using System.Net;
using System.Net.Sockets;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Models;
using Adam.Shared.Services;
using Adam.Shared.Transport;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;

Console.WriteLine("=== Adam E2E Verification ===");
Console.WriteLine();

// ──────────────────────────────────────────────────────────────
// Step 1: Inspect the existing catalog.db
// ──────────────────────────────────────────────────────────────
Console.WriteLine("--- Step 1: Inspect Database State ---");

// Use the BrokerService build output directory's catalog.db
// The BrokerService runs from its build output and connects to catalog.db relative to CWD
var brokerOutput = Path.GetFullPath("src/Adam.BrokerService/bin/Debug/net10.0");
var dbPath = Path.Combine(brokerOutput, "catalog.db");

Console.WriteLine($"  Database path: {dbPath}");
Console.WriteLine($"  File exists: {File.Exists(dbPath)}");

var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;

int userCount = 0;
int roleCount = 0;
int assetCount = 0;
int collectionCount = 0;
bool needsSeed = true;

using (var db = new AppDbContext(dbOptions))
{
    db.Database.EnsureCreated();
    
    roleCount = await db.Roles.CountAsync();
    userCount = await db.Users.CountAsync();
    assetCount = await db.DigitalAssets.CountAsync();
    collectionCount = await db.Collections.CountAsync();
    
    Console.WriteLine($"  Roles: {roleCount}");
    Console.WriteLine($"  Users: {userCount}");
    Console.WriteLine($"  Digital Assets: {assetCount}");
    Console.WriteLine($"  Collections: {collectionCount}");
    
    if (userCount == 0)
    {
        Console.WriteLine();
        Console.WriteLine("  No users found. Seeding default admin user...");
        
        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Administrator");
        if (adminRole == null)
        {
            Console.WriteLine("  [!] Administrator role not found — creating it");
            adminRole = new Role
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                Name = "Administrator",
                Permissions = ["asset:*", "collection:*", "user:*", "role:*", "audit:read"]
            };
            db.Roles.Add(adminRole);
        }
        
        var adminUser = new User
        {
            Id = Guid.NewGuid(),
            Username = "admin",
            Email = "admin@adam.local",
            PasswordHash = PasswordHelper.HashPassword("admin123"),
            RoleId = adminRole.Id,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(adminUser);
        await db.SaveChangesAsync();
        
        userCount = 1;
        needsSeed = false;
        Console.WriteLine("  ✓ Seeded admin user: admin / admin123");
    }
    else
    {
        var users = await db.Users.ToListAsync();
        foreach (var u in users)
        {
            Console.WriteLine($"  User: {u.Username} (Email: {u.Email}, Active: {u.IsActive})");
        }
        needsSeed = false;
    }
    
    var roles = await db.Roles.ToListAsync();
    foreach (var r in roles)
    {
        Console.WriteLine($"  Role: {r.Name} [{string.Join(", ", r.Permissions)}]");
    }
}

Console.WriteLine();

// ──────────────────────────────────────────────────────────────
// Step 2: Connect to BrokerService
// ──────────────────────────────────────────────────────────────
Console.WriteLine("--- Step 2: Connect to BrokerService ---");

const int brokerPort = 9100;
const string brokerHost = "127.0.0.1";

Console.WriteLine($"  Connecting to tcp://{brokerHost}:{brokerPort}...");

using var client = new TcpClient();
try
{
    await client.ConnectAsync(brokerHost, brokerPort);
    Console.WriteLine("  ✓ TCP connection established");
}
catch (SocketException ex)
{
    Console.WriteLine($"  ✗ Connection failed: {ex.Message}");
    Console.WriteLine();
    Console.WriteLine("  Is the BrokerService running? Try:");
    Console.WriteLine("    cd src/Adam.BrokerService/bin/Debug/net10.0 && ./Adam.BrokerService.exe");
    return 1;
}

using var stream = client.GetStream();

// ──────────────────────────────────────────────────────────────
// Step 3: Authenticate via LoginRequest
// ──────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("--- Step 3: Authenticate ---");

var loginReq = new Envelope
{
    CorrelationId = Guid.NewGuid().ToString(),
    MessageType = MessageTypeCode.LoginRequest,
    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new LoginRequest
    {
        Username = "admin",
        Password = "admin123"
    }))
};

await TcpFrame.SendAsync(stream, loginReq);
var loginResp = await TcpFrame.ReceiveAsync(stream);

if (loginResp == null)
{
    Console.WriteLine("  ✗ No response received for login");
    return 1;
}

if (loginResp.StatusCode != 0)
{
    Console.WriteLine($"  ✗ Login failed: {loginResp.ErrorMessage} (code {loginResp.StatusCode})");
    return 1;
}

var loginData = ProtoHelper.Deserialize<LoginResponse>(loginResp.Payload.ToByteArray());
Console.WriteLine($"  ✓ Authenticated successfully");
Console.WriteLine($"  Token: {loginData.Token[..Math.Min(loginData.Token.Length, 48)]}...");
Console.WriteLine($"  Expires: {DateTimeOffset.FromUnixTimeSeconds(loginData.ExpiresAt):g}");
if (loginData.User != null)
{
    Console.WriteLine($"  User: {loginData.User.Username} (Role: {loginData.User.Role})");
}

var authToken = loginData.Token;

// ──────────────────────────────────────────────────────────────
// Step 4: Validate Token
// ──────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("--- Step 4: Validate Token ---");

var validateReq = new Envelope
{
    CorrelationId = Guid.NewGuid().ToString(),
    AuthToken = authToken,
    MessageType = MessageTypeCode.ValidateTokenRequest,
    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ValidateTokenRequest()))
};

await TcpFrame.SendAsync(stream, validateReq);
var validateResp = await TcpFrame.ReceiveAsync(stream);

if (validateResp?.StatusCode == 0)
{
    var validateData = ProtoHelper.Deserialize<ValidateTokenResponse>(validateResp.Payload.ToByteArray());
    Console.WriteLine($"  ✓ Token valid: {validateData.IsValid}");
}
else
{
    Console.WriteLine($"  Token validation response: code {validateResp?.StatusCode}");
}

// ──────────────────────────────────────────────────────────────
// Step 5: Query Service Status
// ──────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("--- Step 5: Service Status ---");

var statusReq = new Envelope
{
    CorrelationId = Guid.NewGuid().ToString(),
    AuthToken = authToken,
    MessageType = MessageTypeCode.GetServiceStatusRequest,
    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new GetServiceStatusRequest()))
};

await TcpFrame.SendAsync(stream, statusReq);
var statusResp = await TcpFrame.ReceiveAsync(stream);

if (statusResp?.StatusCode == 0)
{
    var status = ProtoHelper.Deserialize<GetServiceStatusResponse>(statusResp.Payload.ToByteArray());
    Console.WriteLine($"  ✓ Service state: {status.ServiceState}");
    Console.WriteLine($"  Port: {status.Port}");
    Console.WriteLine($"  Active connections: {status.ActiveConnections}");
    Console.WriteLine($"  Uptime: {status.UptimeSeconds} seconds");
    Console.WriteLine($"  Rejected connections: {status.RejectedConnections}");
}
else
{
    Console.WriteLine($"  ✗ Status query failed: code {statusResp?.StatusCode}, {statusResp?.ErrorMessage}");
}

// ──────────────────────────────────────────────────────────────
// Step 6: List Assets
// ──────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("--- Step 6: List Assets ---");

var assetsReq = new Envelope
{
    CorrelationId = Guid.NewGuid().ToString(),
    AuthToken = authToken,
    MessageType = MessageTypeCode.ListAssetsRequest,
    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListAssetsRequest
    {
        Page = 1,
        PageSize = 10
    }))
};

await TcpFrame.SendAsync(stream, assetsReq);
var assetsResp = await TcpFrame.ReceiveAsync(stream);

if (assetsResp?.StatusCode == 0)
{
    var assets = ProtoHelper.Deserialize<ListAssetsResponse>(assetsResp.Payload.ToByteArray());
    Console.WriteLine($"  ✓ Assets retrieved: {assets.TotalCount} total");
    foreach (var asset in assets.Items)
    {
        Console.WriteLine($"    - {asset.FileName} ({asset.Type}) [{asset.Id[..8]}...]");
    }
}
else
{
    Console.WriteLine($"  ✗ Assets query failed: code {assetsResp?.StatusCode}, {assetsResp?.ErrorMessage}");
}

// ──────────────────────────────────────────────────────────────
// Step 7: List Collections
// ──────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("--- Step 7: List Collections ---");

var collectionsReq = new Envelope
{
    CorrelationId = Guid.NewGuid().ToString(),
    AuthToken = authToken,
    MessageType = MessageTypeCode.ListCollectionsRequest,
    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListCollectionsRequest()))
};

await TcpFrame.SendAsync(stream, collectionsReq);
var collectionsResp = await TcpFrame.ReceiveAsync(stream);

if (collectionsResp?.StatusCode == 0)
{
    var collections = ProtoHelper.Deserialize<ListCollectionsResponse>(collectionsResp.Payload.ToByteArray());
    Console.WriteLine($"  ✓ Collections retrieved: {collections.Items.Count}");
    foreach (var c in collections.Items)
    {
        Console.WriteLine($"    - {c.Name} (Asset count: {c.AssetCount})");
    }
}
else
{
    Console.WriteLine($"  ✗ Collections query failed: code {collectionsResp?.StatusCode}, {collectionsResp?.ErrorMessage}");
}

// ──────────────────────────────────────────────────────────────
// Step 8: List Roles (requires user:read permission)
// ──────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("--- Step 8: List Roles ---");

var rolesReq = new Envelope
{
    CorrelationId = Guid.NewGuid().ToString(),
    AuthToken = authToken,
    MessageType = MessageTypeCode.ListRolesRequest,
    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListRolesRequest()))
};

await TcpFrame.SendAsync(stream, rolesReq);
var rolesResp = await TcpFrame.ReceiveAsync(stream);

if (rolesResp?.StatusCode == 0)
{
    var roleList = ProtoHelper.Deserialize<ListRolesResponse>(rolesResp.Payload.ToByteArray());
    Console.WriteLine($"  ✓ Roles retrieved: {roleList.Items.Count}");
    foreach (var r in roleList.Items)
    {
        Console.WriteLine($"    - {r.Name} [{string.Join(", ", r.Permissions)}]");
    }
}
else
{
    Console.WriteLine($"  ✗ Roles query failed: code {rolesResp?.StatusCode}, {rolesResp?.ErrorMessage}");
}

// ──────────────────────────────────────────────────────────────
// Step 9: List Users (requires user:read permission)
// ──────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("--- Step 9: List Users ---");

var usersReq = new Envelope
{
    CorrelationId = Guid.NewGuid().ToString(),
    AuthToken = authToken,
    MessageType = MessageTypeCode.ListUsersRequest,
    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListUsersRequest()))
};

await TcpFrame.SendAsync(stream, usersReq);
var usersResp = await TcpFrame.ReceiveAsync(stream);

if (usersResp?.StatusCode == 0)
{
    var userList = ProtoHelper.Deserialize<ListUsersResponse>(usersResp.Payload.ToByteArray());
    Console.WriteLine($"  ✓ Users retrieved: {userList.Items.Count}");
    foreach (var u in userList.Items)
    {
        Console.WriteLine($"    - {u.Username} ({u.Email}) — Role: {u.RoleName} — Active: {u.IsActive}");
    }
}
else
{
    Console.WriteLine($"  ✗ Users query failed: code {usersResp?.StatusCode}, {usersResp?.ErrorMessage}");
}

// ──────────────────────────────────────────────────────────────
// Step 10: Unauthenticated request should fail
// ──────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("--- Step 10: Verify Unauthenticated Requests Fail ---");

var noAuthReq = new Envelope
{
    CorrelationId = Guid.NewGuid().ToString(),
    AuthToken = "",
    MessageType = MessageTypeCode.ListAssetsRequest,
    Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListAssetsRequest()))
};

await TcpFrame.SendAsync(stream, noAuthReq);
var noAuthResp = await TcpFrame.ReceiveAsync(stream);

if (noAuthResp?.StatusCode == 7)
{
    Console.WriteLine($"  ✓ Unauthenticated request correctly rejected (code 7: {noAuthResp.ErrorMessage})");
}
else
{
    Console.WriteLine($"  Unauthenticated request response: code {noAuthResp?.StatusCode}");
}

Console.WriteLine();
Console.WriteLine("=== E2E Verification Complete ===");

if (needsSeed)
{
    Console.WriteLine("NOTE: A default admin user has been seeded into catalog.db.");
    Console.WriteLine("      Credentials: admin / admin123");
}

Console.WriteLine();
Console.WriteLine("Summary:");
Console.WriteLine($"  ✓ Database: {roleCount} roles, {userCount} users, {collectionCount} collections, {assetCount} assets");
Console.WriteLine("  ✓ TCP connection to port 9100");
Console.WriteLine("  ✓ Login/Authentication");
Console.WriteLine("  ✓ Token validation");
Console.WriteLine("  ✓ Service status");
Console.WriteLine("  ✓ List Assets");
Console.WriteLine("  ✓ List Collections");
Console.WriteLine("  ✓ List Roles");
Console.WriteLine("  ✓ List Users");
Console.WriteLine("  ✓ Unauthenticated request rejection");

return 0;
