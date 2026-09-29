using HR.Modules.Companies.Contracts;

namespace HR.Modules.Reporting.Tests.Infrastructure;

internal sealed class FakeActiveCompanyDirectory(params Guid[] companyIds) : IActiveCompanyDirectory
{
    private readonly IReadOnlyList<Guid> _companyIds = companyIds;

    public Task<IReadOnlyList<Guid>> GetActiveCompanyIdsAsync(CancellationToken cancellationToken)
        => Task.FromResult(_companyIds);
}
