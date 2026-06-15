using HR.Modules.Employees.Persistence;
using HR.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class ManagerReader(EmployeesDbContext dbContext) : IManagerReader
{
    public async Task<Guid?> GetManagerIdAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken)
    {
        return await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Id == employeeId)
            .Select(e => e.ManagerId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
