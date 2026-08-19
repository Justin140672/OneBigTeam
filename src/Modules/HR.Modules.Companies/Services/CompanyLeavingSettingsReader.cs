using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class CompanyLeavingSettingsReader(CompaniesDbContext dbContext) : ICompanyLeavingSettingsReader
{
    private const bool DefaultAutoDisableAccessOnLeavingDate = true;

    public async Task<bool> GetAutoDisableAccessOnLeavingDateAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        return settings?.AutoDisableAccessOnLeavingDate ?? DefaultAutoDisableAccessOnLeavingDate;
    }
}
