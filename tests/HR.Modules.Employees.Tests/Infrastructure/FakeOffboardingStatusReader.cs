using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeOffboardingStatusReader(OffboardingStatusSummary? summary = null) : IOffboardingStatusReader
{
    public Task<OffboardingStatusSummary?> GetStatusAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken)
        => Task.FromResult(summary);
}
