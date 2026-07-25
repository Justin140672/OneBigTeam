using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeCompanyLeavingSettingsReader(bool autoDisableAccessOnLeavingDate = false)
    : ICompanyLeavingSettingsReader
{
    public Task<bool> GetAutoDisableAccessOnLeavingDateAsync(Guid companyId, CancellationToken cancellationToken) =>
        Task.FromResult(autoDisableAccessOnLeavingDate);
}
