using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Identity.Tests.Infrastructure;

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
