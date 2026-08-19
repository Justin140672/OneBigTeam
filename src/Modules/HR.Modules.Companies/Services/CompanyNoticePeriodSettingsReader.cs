using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class CompanyNoticePeriodSettingsReader(CompaniesDbContext dbContext) : ICompanyNoticePeriodSettingsReader
{
    private const NoticePeriodUnit DefaultNoticePeriodUnit = NoticePeriodUnit.Months;
    private const int DefaultNoticePeriodLength = 1;

    public async Task<(NoticePeriodUnit Unit, int Length)> GetDefaultNoticePeriodAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        return settings is null
            ? (DefaultNoticePeriodUnit, DefaultNoticePeriodLength)
            : (settings.NoticePeriodUnit, settings.NoticePeriodLength);
    }
}
