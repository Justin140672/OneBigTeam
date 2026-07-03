using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class CompanySicknessSettingsReader(CompaniesDbContext dbContext) : ICompanySicknessSettingsReader
{
    public async Task<CompanySicknessSettings> GetSicknessSettingsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (settings is null)
            return CompanySicknessSettings.Default;

        return new CompanySicknessSettings(
            settings.ExcludePublicHolidaysFromSickness,
            settings.FitNoteRequiredAfterDays,
            settings.ReturnToWorkRequiredAfterDays);
    }
}
