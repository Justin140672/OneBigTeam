using HR.Modules.Companies.Contracts;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeCompanyWorkEmailSettingsReader(CompanyWorkEmailSettings? settings = null)
    : ICompanyWorkEmailSettingsReader
{
    public Guid? LastCompanyId { get; private set; }

    public Task<CompanyWorkEmailSettings> GetWorkEmailSettingsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        return Task.FromResult(settings ?? CompanyWorkEmailSettings.Default);
    }
}
