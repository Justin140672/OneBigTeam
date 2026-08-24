using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class EmployeeProbationDatesReader(EmployeesDbContext dbContext) : IEmployeeProbationDatesReader
{
    public async Task<EmployeeProbationDates?> GetProbationDatesAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Id == employeeId)
            .Select(e => new { e.StartDate, e.ProbationEndDate })
            .FirstOrDefaultAsync(cancellationToken);

        return employee is null ? null : new EmployeeProbationDates(employee.StartDate, employee.ProbationEndDate);
    }
}
