using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeSicknessCategoryDefaultsProvisioner : ISicknessCategoryDefaultsProvisioner
{
    public int CallCount { get; private set; }
    public List<Guid> RequestedCompanyIds { get; } = [];

    public Task EnsureDefaultSicknessCategoriesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        CallCount++;
        RequestedCompanyIds.Add(companyId);
        return Task.CompletedTask;
    }
}
