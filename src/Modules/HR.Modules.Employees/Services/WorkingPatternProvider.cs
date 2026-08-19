using HR.Modules.Employees.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class WorkingPatternProvider(
    EmployeesDbContext dbContext,
    ICompanyLeaveSettingsReader companySettingsReader) : IWorkingPatternProvider
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

        if (employee?.PositionProfileId is not null)
        {
            var profile = await dbContext.PositionProfiles
                .AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == employee.PositionProfileId && p.CompanyId == companyId, cancellationToken);

            if (profile?.WorkingDaysOverride is not null && profile.HoursPerDayOverride is not null)
                return new WorkingPattern(profile.WorkingDaysOverride.Value, profile.HoursPerDayOverride.Value);
        }

        var settings = await companySettingsReader.GetLeaveSettingsAsync(companyId, cancellationToken);

        return settings.WorkingPattern;
    }
}
