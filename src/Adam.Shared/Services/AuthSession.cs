using Adam.Shared.Contracts;
using Google.Protobuf;

namespace Adam.Shared.Services;

public sealed class AuthSession : IAuthSession
{
    private readonly BrokerClient _broker;

    public AuthSession(BrokerClient broker)
    {
        _broker = broker;
    }

    public string? Token { get; private set; }
    public UserProfile? CurrentUser { get; private set; }
    public bool IsLoggedIn => Token != null && CurrentUser != null;
    public long TokenExpiresAt { get; private set; }

    public async Task<bool> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (!_broker.IsConnected)
            await _broker.ConnectAsync(ct);

        var correlationId = Guid.NewGuid().ToString();
        var request = new Envelope
        {
            CorrelationId = correlationId,
            MessageType = MessageTypeCode.LoginRequest,
            Payload = ByteString.CopyFrom(
                ProtoHelper.Serialize(new LoginRequest { Username = username, Password = password }))
        };

        var response = await _broker.SendAsync(request, ct);

        if (response.StatusCode != 0)
            return false;

        var loginResponse = ProtoHelper.Deserialize<LoginResponse>(response.Payload.ToByteArray());
        Token = loginResponse.Token;
        CurrentUser = loginResponse.User;
        TokenExpiresAt = loginResponse.ExpiresAt;
        return true;
    }

    public void Logout()
    {
        Token = null;
        CurrentUser = null;
        TokenExpiresAt = 0;
    }

    public bool IsTokenExpired()
    {
        if (TokenExpiresAt == 0) return true;
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= TokenExpiresAt;
    }
}
