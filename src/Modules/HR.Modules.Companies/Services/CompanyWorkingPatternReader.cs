using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class CompanyWorkingPatternReader(CompaniesDbContext dbContext) : ICompanyWorkingPatternReader
{
    public async Task<WorkingPattern?> GetCompanyWorkingPatternAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        return settings is null
            ? null
            : new WorkingPattern(settings.WorkingDays, settings.HoursPerDay);
    }
}
