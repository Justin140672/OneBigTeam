using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Leave.Tests.Infrastructure;

internal sealed class FakeCompanyLeaveSettingsReader(CompanyLeaveSettings? settings = null) : ICompanyLeaveSettingsReader
{
    private readonly CompanyLeaveSettings _settings = settings ?? CompanyLeaveSettings.Default;

    public Task<CompanyLeaveSettings> GetLeaveSettingsAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(_settings);
}
