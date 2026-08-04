using HR.Infrastructure.Abstractions;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class FakeCompanyProvisioner : ICompanyProvisioner
{
    public List<string> ProvisionedCompanyNames { get; } = [];

    public Guid? CompanyIdToReturn { get; set; }

    public int CallCount { get; private set; }

    public Task<Guid> ProvisionCompanyAsync(string companyName, CancellationToken cancellationToken)
    {
        CallCount++;
        ProvisionedCompanyNames.Add(companyName);
        return Task.FromResult(CompanyIdToReturn ?? Guid.NewGuid());
    }

    public List<Guid> DeactivatedCompanyIds { get; } = [];

    public Task DeactivateCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        DeactivatedCompanyIds.Add(companyId);
        return Task.CompletedTask;
    }

    public HashSet<Guid> ActiveCompanyIds { get; } = [];

    public List<Guid> ActivatedCompanyIds { get; } = [];

    public int IsCompanyActiveCallCount { get; private set; }

    public int ActivateCompanyCallCount { get; private set; }

    public Task<bool> IsCompanyActiveAsync(Guid companyId, CancellationToken cancellationToken)
    {
        IsCompanyActiveCallCount++;
        return Task.FromResult(ActiveCompanyIds.Contains(companyId));
    }

    public Task ActivateCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        ActivateCompanyCallCount++;
        ActivatedCompanyIds.Add(companyId);
        ActiveCompanyIds.Add(companyId);
        return Task.CompletedTask;
    }
}
