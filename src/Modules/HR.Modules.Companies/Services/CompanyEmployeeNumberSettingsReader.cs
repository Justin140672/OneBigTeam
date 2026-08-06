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

        // A company with no persisted company_settings row (never explicitly saved) must default
        // the same way Domain.CompanySettings.CreateDefault does (Automatic) — GetHrSettingsHandler,
        // which drives what the HR Settings page and EmployeeEdit.razor display, already falls back
        // to CreateDefault() for exactly this case. Defaulting to Manual here instead disagreed with
        // what the UI showed: the screen said "Automatic" while CreateEmployeeHandler (which reads
        // the mode via this method) silently enforced Manual's "Employee number is required" rule.
        return settings?.EmployeeNumberMode ?? EmployeeNumberMode.Automatic;
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
