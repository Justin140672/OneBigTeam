using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="IUserEmailDirectoryReader"/> — lets GetAuditLogHandler tests control
/// which Supabase auth user ids resolve to which emails (and which ids a given "administrator email"
/// search term matches) without a real IdentityDbContext/database. Same "Companies.Tests Infrastructure
/// fake for an Infrastructure.Abstractions port implemented by Identity" shape as
/// FakeCompanyUserEmailSearchReader.
/// </summary>
internal sealed class FakeUserEmailDirectoryReader : IUserEmailDirectoryReader
{
    public IReadOnlyDictionary<Guid, string> EmailsByUserId { get; set; } = new Dictionary<Guid, string>();

    public IReadOnlyCollection<Guid> UserIdsToReturnForEmailSearch { get; set; } = [];

    public string? LastEmailSearchTerm { get; private set; }

    public Task<IReadOnlyDictionary<Guid, string>> GetEmailsByUserIdsAsync(
        IReadOnlyCollection<Guid> supabaseAuthUserIds,
        CancellationToken cancellationToken)
    {
        var result = EmailsByUserId
            .Where(kvp => supabaseAuthUserIds.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return Task.FromResult<IReadOnlyDictionary<Guid, string>>(result);
    }

    public Task<IReadOnlyCollection<Guid>> FindUserIdsByEmailAsync(
        string searchTerm,
        CancellationToken cancellationToken)
    {
        LastEmailSearchTerm = searchTerm;
        return Task.FromResult(UserIdsToReturnForEmailSearch);
    }
}
