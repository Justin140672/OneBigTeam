using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Tests.Infrastructure;

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
