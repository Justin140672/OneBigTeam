using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

// Simple in-memory stand-in for the Identity-module-backed IEmployeeUserAccountStatusReader,
// following the same "not found = absent" convention as the real implementation. Mirrors
// HR.Modules.Employees.Tests.Infrastructure.FakeEmployeeUserAccountStatusReader.
internal sealed class FakeEmployeeUserAccountStatusReader : IEmployeeUserAccountStatusReader
{
    public Dictionary<Guid, EmployeeUserAccountSummary> Statuses { get; } = new();

    public Task<IReadOnlyDictionary<Guid, EmployeeUserAccountSummary>> GetStatusesAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        var ids = employeeIds.ToHashSet();
        var result = Statuses
            .Where(kvp => ids.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return Task.FromResult<IReadOnlyDictionary<Guid, EmployeeUserAccountSummary>>(result);
    }
}
