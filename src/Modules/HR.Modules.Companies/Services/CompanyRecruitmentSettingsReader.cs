using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class CompanyRecruitmentSettingsReader(CompaniesDbContext dbContext) : ICompanyRecruitmentSettingsReader
{
    public async Task<CompanyRecruitmentSettings> GetRecruitmentSettingsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (settings is null)
            return CompanyRecruitmentSettings.Default;

        return new CompanyRecruitmentSettings(
            settings.VacancyApprovalRequired,
            settings.OfferApprovalRequired,
            settings.CandidateRetentionDays);
    }
}
