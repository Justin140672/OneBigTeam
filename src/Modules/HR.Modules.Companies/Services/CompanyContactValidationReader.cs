using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class CompanyContactValidationReader(CompaniesDbContext dbContext) : ICompanyContactValidationReader
{
    public async Task<CompanyContactValidationRules> GetContactValidationRulesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (settings is null)
            return new CompanyContactValidationRules(
                UkContactRegexDefaults.Postcode,
                UkContactRegexDefaults.Telephone,
                UkContactRegexDefaults.Mobile);

        return new CompanyContactValidationRules(
            settings.PostcodeRegex,
            settings.TelephoneRegex,
            settings.MobileRegex);
    }
}
