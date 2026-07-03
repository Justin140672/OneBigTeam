using HR.Modules.Employees.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class EmployeeNameReader(EmployeesDbContext dbContext) : IEmployeeNameReader
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        var ids = employeeIds.Distinct().ToList();

        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        return await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && ids.Contains(e.Id))
            .ToDictionaryAsync(
                e => e.Id,
                e => $"{e.FirstName} {e.LastName}".Trim(),
                cancellationToken);
    }
}
