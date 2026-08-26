using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class CompanyNotificationSettingsReader(CompaniesDbContext dbContext) : ICompanyNotificationSettingsReader
{
    public async Task<CompanyNotificationSettings> GetNotificationSettingsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (settings is null)
            return CompanyNotificationSettings.Default;

        return new CompanyNotificationSettings(
            settings.EmailNotificationsEnabled,
            settings.ScheduledRemindersEnabled);
    }
}
