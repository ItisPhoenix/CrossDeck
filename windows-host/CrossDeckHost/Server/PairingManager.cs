using System.Security.Cryptography;

namespace CrossDeckHost.Server;

public class PairingManager
{
    private static readonly TimeSpan PinLifetime = TimeSpan.FromMinutes(5);

    private string _currentPin = "000000";
    private DateTime _pinExpiresAt = DateTime.MinValue;
    private readonly HashSet<string> _validTokens = new();

    public string CurrentPin => _currentPin;

    public void GenerateNewPin()
    {
        _currentPin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        _pinExpiresAt = DateTime.UtcNow.Add(PinLifetime);
    }

    public bool ValidatePin(string pin)
    {
        if (DateTime.UtcNow > _pinExpiresAt) return false;
        return pin == _currentPin;
    }

    public string IssueToken()
    {
        var token = Guid.NewGuid().ToString();
        _validTokens.Add(token);
        return token;
    }

    public bool ValidateToken(string token) => _validTokens.Contains(token);

    /// <summary>Milestone 2: call this from a "revoke device" UI action.</summary>
    public void RevokeToken(string token) => _validTokens.Remove(token);
}
