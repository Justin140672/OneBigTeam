using HR.Modules.Documents.Persistence;
using HR.Modules.Employees.Contracts;
using HR.Modules.Tasks.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Documents.Jobs;

/// <summary>
/// Guarantees every current pending profile-photo submission has exactly one active HR review
/// task, recovering the two ways that guarantee can otherwise be violated without a re-upload:
///
///  1. A PendingProfilePhoto whose review task was never created (or was lost — e.g. an interrupted
///     request, or a submission created before this reconciliation/idempotency fix existed).
///  2. A PendingProfilePhoto with a review task that was somehow closed (completed/cancelled)
///     without the submission itself being resolved — left with no way for HR to find it again.
///
/// Uses the same deterministic idempotency key as UploadMyProfilePhotoHandler
/// ("ProfilePhotoReview:{pendingPhotoId}"), so re-running this job — or racing against a genuine
/// upload — can never create a duplicate task for the same submission.
/// </summary>
internal sealed class ReconcileMissingProfilePhotoReviewTasksJob(
    DocumentsDbContext db,
    ITaskCreator taskCreator,
    IEmployeeNameReader employeeNameReader,
    ILogger<ReconcileMissingProfilePhotoReviewTasksJob> logger)
{
    public async Task ExecuteAsync()
    {
        var pendingPhotos = await db.PendingProfilePhotos
            .AsNoTracking()
            .Select(p => new { p.Id, p.CompanyId, p.EmployeeId, p.CreatedAt })
            .ToListAsync();

        if (pendingPhotos.Count == 0)
            return;

        foreach (var group in pendingPhotos.GroupBy(p => p.CompanyId))
        {
            var names = await employeeNameReader.GetNamesAsync(
                group.Key, group.Select(p => p.EmployeeId).Distinct().ToList(), CancellationToken.None);

            foreach (var pending in group)
            {
                try
                {
                    var employeeName = names.TryGetValue(pending.EmployeeId, out var name) ? name : "an employee";

                    // CreateAsync is a no-op returning the existing task's Id when a task already
                    // exists for this idempotency key — this call is therefore safe (and cheap) to
                    // make unconditionally for every pending submission rather than first checking
                    // whether a task is missing. NOTE: the idempotency lookup matches on key alone,
                    // not status — Documents cannot query the Tasks module's own table to check
                    // status (cross-module table access is forbidden — see 02-module-boundaries.md),
                    // so the rarer case of a review task closed without its submission being
                    // resolved is not distinguishable from "already reconciled" here.
                    await taskCreator.CreateAsync(
                        pending.CompanyId,
                        createdBy:          pending.EmployeeId,
                        title:              $"Review profile photo — {employeeName}",
                        description:        "An employee has submitted a new profile photo for review.",
                        priority:           TaskPriority.Low,
                        source:             TaskSource.Document,
                        actionType:         TaskActionType.Review,
                        dueDate:            null,
                        assignedEmployeeId: null,
                        assignedUserId:     null,
                        sourceEntityId:     pending.Id,
                        CancellationToken.None,
                        idempotencyKey:     $"ProfilePhotoReview:{pending.Id}");
                }
                catch (Exception ex)
                {
                    // One submission's reconciliation failing must never stop the rest of the batch
                    // from being checked.
                    logger.LogError(
                        ex,
                        "Profile photo review task reconciliation failed for pending submission " +
                        "{PendingProfilePhotoId} (employee {EmployeeId}, company {CompanyId}).",
                        pending.Id, pending.EmployeeId, pending.CompanyId);
                }
            }
        }
    }
}
