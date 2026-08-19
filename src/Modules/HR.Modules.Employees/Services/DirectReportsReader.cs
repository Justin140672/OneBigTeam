using HR.Modules.Employees.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class DirectReportsReader(EmployeesDbContext dbContext) : IDirectReportsReader
{
    public async Task<IReadOnlyList<Guid>> GetDirectReportIdsAsync(
        Guid companyId,
        Guid managerId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.ManagerId == managerId)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
    }
}
