using System.ComponentModel;
using System.Runtime.CompilerServices;
using Adam.Shared.Contracts;
using Google.Protobuf;

namespace Adam.Shared.Services;

public sealed class AuthSession : IAuthSession
{
    private readonly BrokerClient _broker;
    private UserProfile? _currentUser;

    public AuthSession(BrokerClient broker)
    {
        _broker = broker;
    }

    public string? Token { get; private set; }

    public UserProfile? CurrentUser
    {
        get => _currentUser;
        private set
        {
            if (!ReferenceEquals(_currentUser, value))
            {
                _currentUser = value;
                OnPropertyChanged();
            }
        }
    }

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

    /// <summary>
    /// Validates the current token with the broker and returns the current user profile.
    /// If the account was deactivated or the token is invalid, clears the session and returns null.
    /// Used for periodic session health checks (T7.5).
    /// </summary>
    public async Task<UserProfile?> ValidateTokenAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(Token) || !_broker.IsConnected)
            return null;

        try
        {
            var request = new Envelope
            {
                AuthToken = Token,
                CorrelationId = Guid.NewGuid().ToString(),
                MessageType = MessageTypeCode.ValidateTokenRequest
            };

            var response = await _broker.SendAsync(request, ct);

            if (response.StatusCode != 0)
            {
                // Status code 7 = account deactivated
                if (response.StatusCode == 7)
                {
                    Logout();
                }
                return null;
            }

            var validation = ProtoHelper.Deserialize<ValidateTokenResponse>(response.Payload.ToByteArray());
            if (validation.IsValid && validation.User != null)
            {
                // Check if role changed
                var oldRole = CurrentUser?.Role;
                if (oldRole != validation.User.Role)
                {
                    CurrentUser = validation.User;
                }
                return validation.User;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
