using HR.Modules.Companies.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Services;

internal sealed class CompanyDocumentReminderSettingsReader(CompaniesDbContext dbContext) : ICompanyDocumentReminderSettingsReader
{
    public async Task<CompanyDocumentReminderSettings> GetDocumentReminderSettingsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.CompanySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

        if (settings is null)
            return CompanyDocumentReminderSettings.Default;

        return new CompanyDocumentReminderSettings(
            settings.DocumentRemindersEnabled,
            settings.DocumentReminderOffsetDays1,
            settings.DocumentReminderOffsetDays2,
            settings.DocumentReminderOffsetDays3);
    }
}
