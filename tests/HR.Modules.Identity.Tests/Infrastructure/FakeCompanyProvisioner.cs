using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class FakeCompanyProvisioner : ICompanyProvisioner
{
    public List<string> ProvisionedCompanyNames { get; } = [];

    public Guid? CompanyIdToReturn { get; set; }

    public int CallCount { get; private set; }

    public List<Guid?> RequestedCompanyIds { get; } = [];

    public Exception? ThrowOnProvision { get; set; }

    public List<CompanyProvisioningAdmin> ProvisionedAdmins { get; } = [];

    public Task<Guid> ProvisionCompanyAsync(
        string companyName, CompanyProvisioningAdmin admin, CancellationToken cancellationToken, Guid? companyId = null)
    {
        CallCount++;
        ProvisionedCompanyNames.Add(companyName);
        ProvisionedAdmins.Add(admin);
        RequestedCompanyIds.Add(companyId);
        if (ThrowOnProvision is { } provisionFailure)
        {
            throw provisionFailure;
        }

        return Task.FromResult(CompanyIdToReturn ?? companyId ?? Guid.NewGuid());
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
