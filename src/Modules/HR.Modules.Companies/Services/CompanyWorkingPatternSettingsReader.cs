using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class CompanyWorkingPatternSettingsReader(CompaniesDbContext dbContext) : ICompanyWorkingPatternSettingsReader
{
    public async Task<(int WorkingDayCount, decimal HoursPerDay)> GetDefaultWorkingPatternAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        // Same default-fallback rationale as CompanyEmployeeNumberSettingsReader.GetModeAsync: a
        // company with no persisted company_settings row must default the same way
        // Domain.CompanySettings.CreateDefault does, so this method never disagrees with what
        // GetHrSettingsHandler/the HR Settings page shows.
        if (settings is null)
        {
            var defaultDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
                               WorkingDays.Thursday | WorkingDays.Friday;
            return (CountDays(defaultDays), 7.5m);
        }

        return (CountDays(settings.WorkingDays), settings.HoursPerDay);
    }

    private static int CountDays(WorkingDays workingDays) =>
        Enum.GetValues<WorkingDays>()
            .Where(d => d != WorkingDays.None && workingDays.HasFlag(d))
            .Count();
}
