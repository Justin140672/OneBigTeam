using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeLeavePolicyProvisioner : ILeavePolicyProvisioner
{
    public Guid PolicyIdToReturn { get; set; } = Guid.NewGuid();

    public int CallCount { get; private set; }

    public List<Guid> RequestedCompanyIds { get; } = [];

    public Task<Guid> EnsureDefaultLeavePolicyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        CallCount++;
        RequestedCompanyIds.Add(companyId);
        return Task.FromResult(PolicyIdToReturn);
    }
}
