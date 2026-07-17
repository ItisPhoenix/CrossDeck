using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace CrossDeckHost.Server;

public class PairingManager
{
    private static readonly TimeSpan PinLifetime = TimeSpan.FromMinutes(5);

    private readonly string _tokensFilePath;
    private string _currentPin = "000000";
    private DateTime _pinExpiresAt = DateTime.MinValue;
    private readonly HashSet<string> _validTokens = new();

    // _validTokens is read from ValidateToken on every WebSocket connection and asset request
    // (each on its own thread) while IssueToken/RevokeToken/RevokeAllTokens mutate it concurrently.
    private readonly object _tokensLock = new();

    public string CurrentPin => _currentPin;

    public PairingManager()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CrossDeckHost");
        Directory.CreateDirectory(appDataDir);
        _tokensFilePath = Path.Combine(appDataDir, "tokens.json");
        LoadTokens();
    }

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
        lock (_tokensLock)
        {
            _validTokens.Add(token);
            SaveTokensLocked();
        }
        return token;
    }

    public bool ValidateToken(string token)
    {
        lock (_tokensLock)
        {
            return _validTokens.Contains(token);
        }
    }

    /// <summary>Revokes a single token.</summary>
    public void RevokeToken(string token)
    {
        lock (_tokensLock)
        {
            _validTokens.Remove(token);
            SaveTokensLocked();
        }
    }

    /// <summary>
    /// Revokes every paired device at once — CrossDeck is one-phone-per-PC in v1 (see
    /// MASTER-PLAN.md locked decisions), so "revoke device" from the tray just clears every
    /// token rather than needing per-device selection UI.
    /// </summary>
    public void RevokeAllTokens()
    {
        lock (_tokensLock)
        {
            _validTokens.Clear();
            SaveTokensLocked();
        }
    }

    private void LoadTokens()
    {
        try
        {
            if (!File.Exists(_tokensFilePath)) return;
            var json = File.ReadAllText(_tokensFilePath);
            var tokens = JsonSerializer.Deserialize<List<string>>(json);
            if (tokens != null)
            {
                lock (_tokensLock)
                {
                    foreach (var t in tokens) _validTokens.Add(t);
                }
            }
        }
        catch { /* corrupt/missing file — start with no tokens, same as a fresh install */ }
    }

    private void SaveTokensLocked()
    {
        try
        {
            File.WriteAllText(_tokensFilePath, JsonSerializer.Serialize(_validTokens));
        }
        catch { /* best effort — a failed save just means tokens don't survive next restart */ }
    }
}
