using HR.Modules.Documents.Features.ProcessDocumentExpiryNotifications;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Documents.Jobs;

internal sealed class DocumentExpiryReminderJob(
    DocumentsDbContext dbContext,
    ProcessDocumentExpiryNotificationsHandler handler,
    ILogger<DocumentExpiryReminderJob> logger)
{
    public async Task ExecuteAsync()
    {
        var companyIds = await dbContext.EmployeeDocuments
            .AsNoTracking()
            .Where(ed => ed.ExpiryDate != null && ed.IsLatestVersion && !ed.IsArchived)
            .Select(ed => ed.CompanyId)
            .Distinct()
            .ToListAsync();

        foreach (var companyId in companyIds)
        {
            try
            {
                var result = await handler.HandleAsync(
                    new ProcessDocumentExpiryNotificationsRequest { CompanyId = companyId },
                    CancellationToken.None);

                if (result.ExpiringSoonCount > 0 || result.ExpiredCount > 0)
                {
                    logger.LogInformation(
                        "Document expiry reminders for company {CompanyId}: 90-day={Reminder90}, " +
                        "30-day={Reminder30}, 7-day={Reminder7}, expired={ExpiredCount}",
                        companyId,
                        result.Reminder90Count,
                        result.Reminder30Count,
                        result.Reminder7Count,
                        result.ExpiredCount);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Document expiry reminder processing failed for company {CompanyId}",
                    companyId);
            }
        }
    }
}
