using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Leave.Tests.Infrastructure;

internal sealed class FakeWorkingPatternProvider(WorkingPattern? pattern = null) : IWorkingPatternProvider
{
    private readonly WorkingPattern _pattern = pattern ?? WorkingPattern.Default;

    public Task<WorkingPattern> GetEffectivePatternAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_pattern);
}
