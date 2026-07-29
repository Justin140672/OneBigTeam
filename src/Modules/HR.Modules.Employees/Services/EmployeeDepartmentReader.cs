using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class EmployeeDepartmentReader(EmployeesDbContext dbContext) : IEmployeeDepartmentReader
{
    public async Task<IReadOnlyDictionary<Guid, EmployeeDepartmentInfo>> GetDepartmentsAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        var ids = employeeIds.ToHashSet();
        if (ids.Count == 0)
            return new Dictionary<Guid, EmployeeDepartmentInfo>();

        var employees = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && ids.Contains(e.Id))
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.DepartmentId })
            .ToListAsync(cancellationToken);

        var departmentIds = employees.Select(e => e.DepartmentId).ToHashSet();

        var departmentNames = departmentIds.Count > 0
            ? await dbContext.Departments
                .AsNoTracking()
                .Where(d => departmentIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        return employees.ToDictionary(
            e => e.Id,
            e => new EmployeeDepartmentInfo(
                e.Id,
                $"{e.FirstName} {e.LastName}",
                e.DepartmentId,
                departmentNames.TryGetValue(e.DepartmentId, out var name) ? name : null));
    }
}
