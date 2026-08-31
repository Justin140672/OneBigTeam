using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// ADM-08 test double for <see cref="IUserEmailDirectoryReader"/>. Resolves a configured
/// user-id -> email map; unknown ids are simply absent (mirroring the real reader), so the
/// governance handlers leave <c>ActorEmail</c> null for them.
/// </summary>
internal sealed class FakeUserEmailDirectoryReader(IReadOnlyDictionary<Guid, string>? emails = null)
    : IUserEmailDirectoryReader
{
    private readonly IReadOnlyDictionary<Guid, string> _emails = emails ?? new Dictionary<Guid, string>();

    public IReadOnlyCollection<Guid>? LastRequestedIds { get; private set; }

    public Task<IReadOnlyDictionary<Guid, string>> GetEmailsByUserIdsAsync(
        IReadOnlyCollection<Guid> supabaseAuthUserIds, CancellationToken cancellationToken)
    {
        LastRequestedIds = supabaseAuthUserIds;
        var result = supabaseAuthUserIds
            .Where(id => _emails.ContainsKey(id))
            .ToDictionary(id => id, id => _emails[id]);
        return Task.FromResult<IReadOnlyDictionary<Guid, string>>(result);
    }

    public Task<IReadOnlyCollection<Guid>> FindUserIdsByEmailAsync(
        string searchTerm, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<Guid>>(
            _emails.Where(kv => kv.Value.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key).ToList());
}
