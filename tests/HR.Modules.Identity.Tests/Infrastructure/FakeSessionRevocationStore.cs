using HR.Modules.Identity.Services;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class FakeSessionRevocationStore : ISessionRevocationStore
{
    private readonly Dictionary<Guid, DateTimeOffset> _revokedAt = [];

    public List<(Guid SupabaseAuthUserId, DateTimeOffset RevokedAt)> RevokeCalls { get; } = [];

    public Task RevokeAsync(Guid supabaseAuthUserId, DateTimeOffset revokedAt, CancellationToken cancellationToken)
    {
        RevokeCalls.Add((supabaseAuthUserId, revokedAt));

        if (!_revokedAt.TryGetValue(supabaseAuthUserId, out var existing) || revokedAt > existing)
        {
            _revokedAt[supabaseAuthUserId] = revokedAt;
        }

        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> GetRevokedAtAsync(Guid supabaseAuthUserId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_revokedAt.TryGetValue(supabaseAuthUserId, out var revokedAt)
            ? revokedAt
            : (DateTimeOffset?)null);
    }
}
