using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class CompanyEmployeeNumberSettingsReader(CompaniesDbContext dbContext) : ICompanyEmployeeNumberSettingsReader
{
    public async Task<EmployeeNumberMode> GetModeAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        return settings?.EmployeeNumberMode ?? EmployeeNumberMode.Manual;
    }

    public async Task<EmployeeNumberSequencePreview> GetSequencePreviewAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (settings is null)
        {
            throw new InvalidOperationException(
                $"Cannot preview employee numbers: company '{companyId}' has no company_settings row.");
        }

        return new EmployeeNumberSequencePreview(
            settings.EmployeeNumberPrefix, settings.NextEmployeeNumber, settings.EmployeeNumberMinimumLength);
    }
}
