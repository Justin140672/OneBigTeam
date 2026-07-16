using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Documents.Jobs;

/// <summary>
/// Daily Hangfire job that detects Shared Company Documents due for review, across all
/// companies. This is a global background job — unlike
/// ListSharedCompanyDocumentsDueForReviewHandler (an HTTP endpoint scoped to a single
/// CompanyId for one company's admin view), this job queries DocumentsDbContext directly with
/// no CompanyId filter, the same way GenerateDueProbationReviewsJob spans every company.
///
/// "Due for review" mirrors that handler's filter: non-Archived, ReviewDate set, and
/// ReviewDate on or before today.
///
/// This module has no separate "review completed" record. Completing a document's review simply
/// means an HR admin moves ReviewDate forward via the Edit Metadata dialog, so a document whose
/// review was already completed naturally falls out of this query once its ReviewDate is in the
/// future — there is nothing extra to track for "ignores completed reviews".
///
/// Scope is intentionally limited to detection: per the ticket, this job finds due documents and
/// logs a summary. It does not create tasks or send notifications — unlike
/// SharedCompanyDocumentAcknowledgementReminderJob, which is a distinct, separately-ticketed
/// concern for acknowledgement reminders.
/// </summary>
internal sealed class DetectDocumentsDueForReviewJob(
    DocumentsDbContext db,
    IClock clock,
    ILogger<DetectDocumentsDueForReviewJob> logger)
{
    public async Task ExecuteAsync()
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);

        var dueDocumentIds = await db.SharedCompanyDocuments
            .AsNoTracking()
            .Where(d => d.Status != SharedCompanyDocumentStatus.Archived
                && d.ReviewDate != null
                && d.ReviewDate <= today)
            .Select(d => d.Id)
            .ToListAsync();

        logger.LogInformation(
            "DetectDocumentsDueForReviewJob found {DueCount} shared company document(s) due for review",
            dueDocumentIds.Count);
    }
}
