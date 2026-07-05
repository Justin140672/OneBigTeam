using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeCompanyLeaveSettingsReader(CompanyLeaveSettings? settings = null) : ICompanyLeaveSettingsReader
{
    private readonly CompanyLeaveSettings _settings = settings ?? CompanyLeaveSettings.Default;

    public Task<CompanyLeaveSettings> GetLeaveSettingsAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(_settings);
}
