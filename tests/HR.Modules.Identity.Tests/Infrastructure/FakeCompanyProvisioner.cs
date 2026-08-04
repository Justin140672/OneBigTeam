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
}
