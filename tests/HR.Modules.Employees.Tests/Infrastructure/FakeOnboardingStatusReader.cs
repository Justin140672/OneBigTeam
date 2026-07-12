using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeOnboardingStatusReader(OnboardingStatusSummary? summary = null) : IOnboardingStatusReader
{
    public Task<OnboardingStatusSummary?> GetStatusAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken)
        => Task.FromResult(summary);
}
