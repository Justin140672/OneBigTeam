using HR.Modules.Companies.Contracts;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>Hand-rolled fake for <see cref="IActiveCompanyDirectory"/> — returns a fixed set of ids.</summary>
internal sealed class FakeActiveCompanyDirectory(params Guid[] companyIds) : IActiveCompanyDirectory
{
    private readonly IReadOnlyList<Guid> _companyIds = companyIds;

    public Task<IReadOnlyList<Guid>> GetActiveCompanyIdsAsync(CancellationToken cancellationToken)
        => Task.FromResult(_companyIds);
}
