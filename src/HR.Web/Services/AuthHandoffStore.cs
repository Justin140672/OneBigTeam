using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace HR.Web.Services;

/// <summary>
/// Server-side, single-use, short-lived exchange store for a freshly established Supabase session.
///
/// The security ticket "Remove authentication tokens from browser-visible URLs" forbids putting
/// access/refresh tokens (or any equivalent credential) into URL query parameters. Blazor Server's
/// interactive circuit cannot set an HttpOnly cookie mid-render, so establishing the session cookie
/// must still happen on a real HTTP request/response hop (a hard browser navigation). Instead of
/// carrying the token itself across that hop, the circuit stashes the session here and carries only
/// an opaque, random, single-use handoff <c>code</c> — worthless in browser history, referrer
/// headers, proxy logs or a screenshot because it is consumed server-side on first use and expires
/// within two minutes.
///
/// Registered as a singleton — it is process-wide and deliberately in-memory only (a handoff that
/// outlives a process restart is not a requirement and would be a larger attack surface).
/// </summary>
public sealed class AuthHandoffStore(TimeProvider timeProvider)
{
    // Two minutes is comfortably longer than the hard-navigation round trip yet short enough that a
    // leaked (but unused) code is not a meaningful credential.
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public sealed record Session(string AccessToken, string RefreshToken, int ExpiresInSeconds);

    private sealed record Entry(Session Session, DateTimeOffset ExpiresAtUtc);

    /// <summary>
    /// Stashes a session and returns the opaque handoff code to carry (in a URL query is acceptable
    /// for this value — it is not a credential and cannot be replayed).
    /// </summary>
    public string Issue(Session session)
    {
        PurgeExpired();

        var code = Base64UrlToken();
        _entries[code] = new Entry(session, timeProvider.GetUtcNow().Add(Ttl));
        return code;
    }

    /// <summary>
    /// Atomically consumes a handoff code. Returns <c>null</c> for an unknown, already-used,
    /// expired or tampered code — callers must treat all of those identically (redirect to /login).
    /// </summary>
    public Session? Redeem(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || !_entries.TryRemove(code, out var entry))
            return null;

        return entry.ExpiresAtUtc < timeProvider.GetUtcNow() ? null : entry.Session;
    }

    private void PurgeExpired()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresAtUtc < now)
                _entries.TryRemove(pair.Key, out _);
        }
    }

    private static string Base64UrlToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
