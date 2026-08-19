using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Sickness.Tests.Infrastructure;

internal sealed class FakeWorkingPatternProvider(WorkingPattern pattern) : IWorkingPatternProvider
{
    public Task<WorkingPattern> GetEffectivePatternAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken)
        => Task.FromResult(pattern);
}
