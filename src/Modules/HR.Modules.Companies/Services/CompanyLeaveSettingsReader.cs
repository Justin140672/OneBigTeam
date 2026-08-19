using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class CompanyLeaveSettingsReader(CompaniesDbContext dbContext) : ICompanyLeaveSettingsReader
{
    public async Task<CompanyLeaveSettings> GetLeaveSettingsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (settings is null)
            return CompanyLeaveSettings.Default;

        return new CompanyLeaveSettings(
            settings.ExcludePublicHolidaysFromLeave,
            settings.LeaveYearStartMonth,
            settings.DefaultHolidayAllowance,
            new WorkingPattern(settings.WorkingDays, settings.HoursPerDay));
    }
}
