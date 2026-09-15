using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace StockPilot.Api.Models;

// In-memory only — lost on every app restart, and never shared across
// multiple server instances. Production needs a shared store (a database
// table or Redis), the same gap InMemoryProductStore had before Day 16.
// A ConcurrentDictionary is used (not a plain Dictionary) because, unlike
// IProductStore's Singleton, this really is accessed concurrently by
// simultaneous requests with no per-request scope to protect it.
public class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly ConcurrentDictionary<string, (string Username, DateTime ExpiresAtUtc)> _tokens = new();
    private readonly TimeSpan _lifetime;

    // Lifetime is a constructor parameter (not a fixed constant) purely so
    // tests can make a token expire on demand — e.g. TimeSpan.FromSeconds(-1)
    // makes a freshly-issued token already expired, with no real waiting
    // required. DI (Program.cs's AddSingleton call) never passes an argument
    // here, so it always gets the real 7-day production default.
    public InMemoryRefreshTokenStore(TimeSpan? lifetime = null)
    {
        _lifetime = lifetime ?? TimeSpan.FromDays(7);
    }

    public string Issue(string username)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _tokens[token] = (username, DateTime.UtcNow.Add(_lifetime));
        return token;
    }

    // TryRemove (not TryGetValue) is what makes a refresh token one-time-use:
    // the moment it's consumed, it's gone — a second attempt with the same
    // token finds nothing, whether that second attempt is a genuine reuse
    // mistake or an attacker replaying a stolen token.
    public bool TryConsume(string refreshToken, out string username)
    {
        username = string.Empty;

        if (!_tokens.TryRemove(refreshToken, out var entry))
        {
            return false;
        }

        if (entry.ExpiresAtUtc < DateTime.UtcNow)
        {
            return false;
        }

        username = entry.Username;
        return true;
    }
}
