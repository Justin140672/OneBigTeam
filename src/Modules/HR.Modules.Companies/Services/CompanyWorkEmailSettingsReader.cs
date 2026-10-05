using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class CompanyWorkEmailSettingsReader(CompaniesDbContext dbContext) : ICompanyWorkEmailSettingsReader
{
    public async Task<CompanyWorkEmailSettings> GetWorkEmailSettingsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (settings is null)
            return CompanyWorkEmailSettings.Default;

        return new CompanyWorkEmailSettings(
            settings.WorkEmailSuggestionsEnabled,
            settings.WorkEmailPrimaryDomain,
            settings.WorkEmailNamingConvention);
    }
}
