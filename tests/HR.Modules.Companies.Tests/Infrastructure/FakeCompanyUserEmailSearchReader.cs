using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="ICompanyUserEmailSearchReader"/> — lets ListCustomersHandler tests
/// control which company ids a given search term "matches by email" without a real
/// IdentityDbContext/database.
/// </summary>
internal sealed class FakeCompanyUserEmailSearchReader : ICompanyUserEmailSearchReader
{
    public IReadOnlyCollection<Guid> CompanyIdsToReturn { get; set; } = [];

    public string? LastSearchTerm { get; private set; }

    public Task<IReadOnlyCollection<Guid>> FindCompanyIdsByEmailAsync(
        string searchTerm,
        CancellationToken cancellationToken)
    {
        LastSearchTerm = searchTerm;
        return Task.FromResult(CompanyIdsToReturn);
    }
}
