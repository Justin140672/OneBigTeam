using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class CurrentEmployeeReader(EmployeesDbContext dbContext) : ICurrentEmployeeReader
{
    public async Task<IReadOnlyList<Guid>> GetCurrentEmployeeIdsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        return await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Status != EmploymentStatus.Terminated)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
    }
}
