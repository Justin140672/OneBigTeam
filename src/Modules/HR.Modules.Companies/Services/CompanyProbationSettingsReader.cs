using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class CompanyProbationSettingsReader(CompaniesDbContext dbContext) : ICompanyProbationSettingsReader
{
    private const int DefaultProbationMonths = 6;

    public async Task<int> GetProbationMonthsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        return settings?.ProbationMonths ?? DefaultProbationMonths;
    }
}
