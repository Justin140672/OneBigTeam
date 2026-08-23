using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class EmployeeStartDateReader(EmployeesDbContext dbContext) : IEmployeeStartDateReader
{
    public async Task<DateOnly?> GetStartDateAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Id == employeeId)
            .Select(e => new { e.StartDate })
            .FirstOrDefaultAsync(cancellationToken);

        return employee?.StartDate;
    }
}
