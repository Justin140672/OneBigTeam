using HR.Modules.Documents.Features.ProcessDocumentExpiryNotifications;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Documents.Jobs;

/// <summary>
/// Daily job (DOC-03) that automatically processes the 90/30/7-day-before-expiry reminder
/// schedule and the overdue/expired notification for every company that has at least one employee
/// document with an expiry date. Replaces reliance on an HR user or external caller manually
/// invoking the /expiry-notifications endpoint — that endpoint is retained for on-demand/admin use
/// but this job is now what runs it automatically, once per day, for every company.
///
/// Mirrors LeaveYearRolloverJob/AttendanceAlertEvaluationJob/GenerateDueProbationReviewsJob's
/// shape: company ids are discovered from this module's own data (no separate "active company"
/// concept exists in the Documents module), each company's threshold evaluation happens in its own
/// configured time zone via <see cref="HR.Modules.Companies.Contracts.ICompanyTimeZoneReader"/>
/// (delegated to inside ProcessDocumentExpiryNotificationsHandler), and one company's failure is
/// isolated so it never blocks the rest of the batch. ProcessDocumentExpiryNotificationsHandler is
/// independently idempotent per stage (see its own doc comment), so a Hangfire retry after a
/// partial failure is always a safe re-run.
/// </summary>
internal sealed class DocumentExpiryReminderJob(
    DocumentsDbContext dbContext,
    ProcessDocumentExpiryNotificationsHandler handler,
    ILogger<DocumentExpiryReminderJob> logger)
{
    public async Task ExecuteAsync()
    {
        var companyIds = await dbContext.EmployeeDocuments
            .AsNoTracking()
            .Where(ed => ed.ExpiryDate != null)
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
                // Isolate one company's failure from the rest of the batch. Hangfire retries the
                // whole job automatically per its default retry policy, and the handler's own
                // per-stage idempotency guard means any document/stage that already fired is a
                // safe no-op on retry — only the failed company's remaining work is repeated.
                logger.LogError(ex,
                    "Document expiry reminder processing failed for company {CompanyId}",
                    companyId);
            }
        }
    }
}
