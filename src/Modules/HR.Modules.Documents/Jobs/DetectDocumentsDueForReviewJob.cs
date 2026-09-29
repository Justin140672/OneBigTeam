using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Employees.Contracts;
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
    ICompanyTimeZoneReader timeZoneReader,
    ITaskCreator taskCreator,
    IOpenTaskBySourceEntityReader openTaskReader,
    IEmployeeNameReader employeeNameReader,
    INotificationWriter notificationWriter,
    ILogger<DetectDocumentsDueForReviewJob> logger)
{
    public async Task ExecuteAsync()
    {
        var allDocumentsWithReviewDate = await db.SharedCompanyDocuments
            .AsNoTracking()
            .Where(d => d.Status != SharedCompanyDocumentStatus.Archived
                && d.Status != SharedCompanyDocumentStatus.Expired
                && d.ReviewDate != null)
            .ToListAsync();

        var candidates = new List<SharedCompanyDocument>();
        foreach (var companyGroup in allDocumentsWithReviewDate.GroupBy(d => d.CompanyId))
        {
            var timeZoneId = await timeZoneReader.GetTimeZoneAsync(companyGroup.Key, CancellationToken.None);
            var today = clock.TodayIn(timeZoneId);
            candidates.AddRange(companyGroup.Where(d => d.ReviewDate <= today));
        }

        logger.LogInformation(
            "DetectDocumentsDueForReviewJob found {DueCount} shared company document(s) due for review",
            candidates.Count);

        candidates = candidates.Where(d => d.ReviewOwnerEmployeeId is not null).ToList();
        var createdCount = 0;

        foreach (var companyGroup in candidates.GroupBy(d => d.CompanyId))
        {
            var companyId = companyGroup.Key;
            var documents = companyGroup.ToList();
            var documentIds = documents.Select(d => d.Id).ToList();

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
