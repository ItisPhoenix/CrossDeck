using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace CrossDeckHost.Server;

public class PairingManager
{
    private static readonly TimeSpan PinLifetime = TimeSpan.FromMinutes(5);
    private const int MaxFailedAttempts = 5;
    private const int MaxPinAttemptEntries = 256;
    private static readonly TimeSpan MaxPinLockout = TimeSpan.FromMinutes(5);

    private readonly string _tokensFilePath;
    private readonly string _tokenStateFilePath;
    private string _tokenEpoch = Guid.NewGuid().ToString("N");
    private string _currentPin = "";
    private DateTime _pinExpiresAt = DateTime.MinValue;
    private readonly HashSet<string> _validTokens = new();
    private readonly Dictionary<string, PinAttemptState> _pinAttempts = new(StringComparer.Ordinal);
    private readonly object _tokensLock = new();
    private bool _persistenceFaulted;

    public string CurrentPin
    {
        get
        {
            lock (_tokensLock)
                return CanPairLocked() ? _currentPin : "";
        }
    }

    public bool CanPair
    {
        get
        {
            lock (_tokensLock)
                return CanPairLocked();
        }
    }

    public bool HasValidTokens
    {
        get
        {
            lock (_tokensLock)
                return !_persistenceFaulted && _validTokens.Count > 0;
        }
    }

    public PairingManager()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CrossDeckHost");
        Directory.CreateDirectory(appDataDir);
        _tokensFilePath = Path.Combine(appDataDir, "tokens.json");
        _tokenStateFilePath = Path.Combine(appDataDir, "token-state.json");
        LoadTokens();
    }

    /// <summary>
    /// Starts a new single-device pairing epoch. Existing tokens are revoked before the new PIN is
    /// exposed. A failed persistence operation leaves pairing unavailable and returns false.
    /// </summary>
    public bool GenerateNewPin()
    {
        lock (_tokensLock)
        {
            if (_persistenceFaulted) return false;

            _validTokens.Clear();
            _tokenEpoch = Guid.NewGuid().ToString("N");
            if (!PersistTokenStateLocked())
            {
                _persistenceFaulted = true;
                _currentPin = "";
                _pinExpiresAt = DateTime.MinValue;
                return false;
            }

            _currentPin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            _pinExpiresAt = DateTime.UtcNow.Add(PinLifetime);
            _pinAttempts.Clear();
            return true;
        }
    }

    /// <summary>
    /// Validates and consumes bootstrap PIN in same lock as token issuance. PIN can mint exactly
    /// one persistent token, even when two clients race concurrently.
    /// </summary>
    public bool TryIssueTokenForPin(string pin, IPAddress sourceAddress, out string token)
    {
        lock (_tokensLock)
        {
            token = "";
            if (_persistenceFaulted) return false;

            var now = DateTime.UtcNow;
            var sourceKey = sourceAddress.ToString();
            if (_pinAttempts.TryGetValue(sourceKey, out var attempt) && now < attempt.LockedUntil)
                return false;

            if (!CanPairLocked()) return false;

            if (pin == _currentPin)
            {
                var candidate = Guid.NewGuid().ToString("D");
                _validTokens.Add(candidate);
                if (!PersistTokenStateLocked())
                {
                    _validTokens.Remove(candidate);
                    _persistenceFaulted = true;
                    return false;
                }

                _currentPin = "";
                _pinExpiresAt = DateTime.MinValue;
                _pinAttempts.Remove(sourceKey);
                token = candidate;
                return true;
            }

            attempt ??= new PinAttemptState();
            attempt.FailedAttempts++;
            attempt.LastAttemptAt = now;
            if (attempt.FailedAttempts >= MaxFailedAttempts)
            {
                var lockoutSeconds = Math.Min(
                    30 * (attempt.FailedAttempts - MaxFailedAttempts + 1),
                    (int)MaxPinLockout.TotalSeconds);
                attempt.LockedUntil = now.AddSeconds(lockoutSeconds);
            }
            _pinAttempts[sourceKey] = attempt;
            TrimPinAttemptsLocked(now);
            return false;
        }
    }

    public bool ValidateToken(string token)
    {
        lock (_tokensLock)
            return !_persistenceFaulted && _validTokens.Contains(token);
    }

    /// <summary>Revokes a single token and restores it in memory if persistence fails.</summary>
    public bool RevokeToken(string token)
    {
        lock (_tokensLock)
        {
            if (!_validTokens.Remove(token)) return true;
            if (PersistTokenStateLocked()) return true;
            _validTokens.Add(token);
            _persistenceFaulted = true;
            return false;
        }
    }

    /// <summary>Revokes all tokens and starts a new invalidation epoch.</summary>
    public bool RevokeAllTokens()
    {
        lock (_tokensLock)
        {
            if (_persistenceFaulted) return false;
            _validTokens.Clear();
            _tokenEpoch = Guid.NewGuid().ToString("N");
            _pinAttempts.Clear();
            if (PersistTokenStateLocked()) return true;
            _persistenceFaulted = true;
            return false;
        }
    }

    private bool CanPairLocked() =>
        !_persistenceFaulted && !string.IsNullOrEmpty(_currentPin) && DateTime.UtcNow <= _pinExpiresAt;

    private void TrimPinAttemptsLocked(DateTime now)
    {
        foreach (var staleKey in _pinAttempts
                     .Where(pair => now - pair.Value.LastAttemptAt > TimeSpan.FromMinutes(15))
                     .Select(pair => pair.Key)
                     .ToList())
            _pinAttempts.Remove(staleKey);

        while (_pinAttempts.Count > MaxPinAttemptEntries)
        {
            var oldest = _pinAttempts.OrderBy(pair => pair.Value.LastAttemptAt).First().Key;
            _pinAttempts.Remove(oldest);
        }
    }

    private void LoadTokens()
    {
        try
        {
            if (!File.Exists(_tokensFilePath)) return;

            var json = File.ReadAllText(_tokensFilePath);
            // v2.1.0 stored a bare token list with no revocation epoch. Do not trust that format
            // after the security upgrade; users complete one deliberate TLS/fingerprint pairing.
            if (json.TrimStart().StartsWith("[", StringComparison.Ordinal)) return;
            var document = JsonSerializer.Deserialize<TokenStoreDocument>(json);

            if (document is null || string.IsNullOrWhiteSpace(document.Epoch))
                throw new InvalidDataException("Token store has no epoch");

            if (!File.Exists(_tokenStateFilePath)) return;
            var marker = File.ReadAllText(_tokenStateFilePath).Trim();
            if (!string.Equals(marker, document.Epoch, StringComparison.Ordinal))
            {
                // A newer revocation marker invalidates an older token document.
                return;
            }

            lock (_tokensLock)
            {
                _tokenEpoch = document.Epoch;
                foreach (var token in document.Tokens.Where(t => !string.IsNullOrWhiteSpace(t)))
                    _validTokens.Add(token);

            }
        }
        catch
        {
            lock (_tokensLock)
            {
                _validTokens.Clear();
                _persistenceFaulted = true;
            }
        }
    }

    private bool PersistTokenStateLocked()
    {
        try
        {
            // Marker is written first. If token-document replacement fails, next startup sees
            // the epoch mismatch and refuses to restore the stale token document.
            WriteAtomic(_tokenStateFilePath, _tokenEpoch + Environment.NewLine);
            var document = new TokenStoreDocument
            {
                Epoch = _tokenEpoch,
                Tokens = _validTokens.OrderBy(token => token, StringComparer.Ordinal).ToList()
            };
            WriteAtomic(_tokensFilePath, JsonSerializer.Serialize(document));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, content);
        File.Move(tempPath, path, true);
    }

    private sealed class TokenStoreDocument
    {
        public string Epoch { get; set; } = "";
        public List<string> Tokens { get; set; } = new();
    }

    private sealed class PinAttemptState
    {
        public int FailedAttempts { get; set; }
        public DateTime LockedUntil { get; set; }
        public DateTime LastAttemptAt { get; set; } = DateTime.UtcNow;
    }
}
