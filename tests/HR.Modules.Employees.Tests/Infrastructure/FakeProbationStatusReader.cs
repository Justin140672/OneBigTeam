using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeProbationStatusReader(ProbationStatusSummary? summary = null) : IProbationStatusReader
{
    public Task<ProbationStatusSummary?> GetStatusAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken)
        => Task.FromResult(summary);
}
