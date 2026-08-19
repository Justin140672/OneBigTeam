using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class CompanyTimeZoneReader(CompaniesDbContext dbContext) : ICompanyTimeZoneReader
{
    public async Task<string> GetTimeZoneAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        return settings?.TimeZone ?? "UTC";
    }
}
