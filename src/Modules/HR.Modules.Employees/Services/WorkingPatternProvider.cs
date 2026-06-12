using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class WorkingPatternProvider(
    EmployeesDbContext dbContext,
    ICompanyWorkingPatternReader companyWorkingPatternReader) : IWorkingPatternProvider
{
    public async Task<WorkingPattern> GetEffectivePatternAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var employee = await dbContext.Employees
            .SingleOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == companyId, cancellationToken);

        if (employee?.WorkingDaysOverride is not null && employee.HoursPerDayOverride is not null)
            return new WorkingPattern(employee.WorkingDaysOverride.Value, employee.HoursPerDayOverride.Value);

        var companyPattern = await companyWorkingPatternReader
            .GetCompanyWorkingPatternAsync(companyId, cancellationToken);

        return companyPattern ?? WorkingPattern.Default;
    }
}
