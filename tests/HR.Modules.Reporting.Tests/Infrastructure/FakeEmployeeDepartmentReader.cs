using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="IEmployeeDepartmentReader"/> — returns a pre-configured
/// dictionary of department info, or an empty dictionary by default so handlers under test fall
/// back to the employee-id-as-name behaviour.
/// </summary>
internal sealed class FakeEmployeeDepartmentReader : IEmployeeDepartmentReader
{
    private readonly IReadOnlyDictionary<Guid, EmployeeDepartmentInfo> _departments;

    public FakeEmployeeDepartmentReader(IReadOnlyDictionary<Guid, EmployeeDepartmentInfo>? departments = null)
    {
        _departments = departments ?? new Dictionary<Guid, EmployeeDepartmentInfo>();
    }

    public Task<IReadOnlyDictionary<Guid, EmployeeDepartmentInfo>> GetDepartmentsAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_departments);
    }
}
