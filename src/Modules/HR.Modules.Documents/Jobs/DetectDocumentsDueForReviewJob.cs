using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Documents.Jobs;

/// <summary>
/// Daily Hangfire job that detects Shared Company Documents due for review, across all
/// companies, and creates a Review task for each one that has a Review Owner assigned. This is
/// a global background job — unlike ListSharedCompanyDocumentsDueForReviewHandler (an HTTP
/// endpoint scoped to a single CompanyId for one company's admin view), this job queries
/// DocumentsDbContext directly with no CompanyId filter, the same way
/// GenerateDueProbationReviewsJob spans every company.
///
/// "Due for review" mirrors that handler's filter: non-Archived, non-Expired, ReviewDate set,
/// and ReviewDate on or before today.
///
/// This module has no separate "review completed" record. Completing a document's review simply
/// means an HR admin moves ReviewDate forward via the Edit Metadata dialog, so a document whose
/// review was already completed naturally falls out of this query once its ReviewDate is in the
/// future — there is nothing extra to track for "ignores completed reviews".
///
/// A due document with no ReviewOwnerEmployeeId is skipped entirely — there is nobody to assign
/// the task to, and this deliberately does not fall back to any other assignee (e.g. document
/// creator/last editor). Duplicate task creation is prevented via IOpenTaskBySourceEntityReader,
/// filtered to TaskActionType.Review specifically: a document can otherwise have many open
/// Acknowledge tasks (one per eligible employee, created by
/// SharedCompanyDocumentAcknowledgementReminderJob) all sharing the same SourceEntityId, so an
/// unfiltered open-task check would wrongly treat those as "already has a task" and skip
/// creating the Review task.
/// </summary>
internal sealed class DetectDocumentsDueForReviewJob(
    DocumentsDbContext db,
    IClock clock,
    ITaskCreator taskCreator,
    IOpenTaskBySourceEntityReader openTaskReader,
    IEmployeeNameReader employeeNameReader,
    INotificationWriter notificationWriter,
    ILogger<DetectDocumentsDueForReviewJob> logger)
{
    public async Task ExecuteAsync()
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);

        var dueDocuments = await db.SharedCompanyDocuments
            .AsNoTracking()
            .Where(d => d.Status != SharedCompanyDocumentStatus.Archived
                && d.Status != SharedCompanyDocumentStatus.Expired
                && d.ReviewDate != null
                && d.ReviewDate <= today)
            .ToListAsync();

        logger.LogInformation(
            "DetectDocumentsDueForReviewJob found {DueCount} shared company document(s) due for review",
            dueDocuments.Count);

        var candidates = dueDocuments.Where(d => d.ReviewOwnerEmployeeId is not null).ToList();
        var createdCount = 0;

        foreach (var companyGroup in candidates.GroupBy(d => d.CompanyId))
        {
            var companyId = companyGroup.Key;
            var documents = companyGroup.ToList();
            var documentIds = documents.Select(d => d.Id).ToList();

            // Batched, company-scoped check for an already-open Review task per document —
            // one call across all candidate document ids for this company, not N+1.
            var openReviewTaskIds = await openTaskReader.GetOpenTaskIdsAsync(
                companyId, documentIds, CancellationToken.None, TaskActionType.Review);

            var reviewOwnerIds = documents.Select(d => d.ReviewOwnerEmployeeId!.Value).Distinct();
            var reviewOwnerNames = await employeeNameReader.GetNamesAsync(companyId, reviewOwnerIds, CancellationToken.None);

            foreach (var document in documents)
            {
                if (openReviewTaskIds.ContainsKey(document.Id))
                    continue;

                var reviewOwnerId = document.ReviewOwnerEmployeeId!.Value;
                var reviewOwnerName = reviewOwnerNames.GetValueOrDefault(reviewOwnerId, "Unknown Employee");
                var description = $"{reviewOwnerName}, please review '{document.Title}'. Review was due {document.ReviewDate:d MMM yyyy}.";

                // notifyAssignee: false — the generic "New task assigned" notification would carry
                // the task's own id as SourceEntityId, not the document's. A dedicated notification is
                // written below instead, with SourceEntityId set to the document's id, so clicking it
                // links directly to the document rather than the task.
                await taskCreator.CreateAsync(
                    companyId,
                    createdBy:          document.CreatedBy,
                    title:              $"Review due: {document.Title}",
                    description:        description,
                    priority:           TaskPriority.Medium,
                    source:             TaskSource.Document,
                    actionType:         TaskActionType.Review,
                    dueDate:            document.ReviewDate,
                    assignedEmployeeId: reviewOwnerId,
                    assignedUserId:     reviewOwnerId,
                    sourceEntityId:     document.Id,
                    CancellationToken.None,
                    notifyAssignee:     false);

                await notificationWriter.WriteAsync(
                    Guid.NewGuid(),
                    companyId,
                    reviewOwnerId,
                    "Review due",
                    description,
                    document.Id,
                    NotificationType.SharedCompanyDocumentReviewDue,
                    NotificationPriority.Normal,
                    clock.UtcNowOffset(),
                    CancellationToken.None);

                createdCount++;
            }
        }

        logger.LogInformation(
            "DetectDocumentsDueForReviewJob created {CreatedCount} review task(s)",
            createdCount);
    }
}
