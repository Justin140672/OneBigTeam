using HR.Modules.Tasks.Contracts;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Services;

/// <summary>
/// PROB-03: recalculates a probation record's not-yet-completed review schedule whenever a
/// relevant date or setting changes (currently: <c>ExpectedEndDate</c> being amended directly via
/// UpdateProbationRecord; also available for a future company-checkpoint-settings-changed
/// consumer). Cancels every still-<c>Pending</c> ManagerCheckIn/HrReview/FinalDecision review for
/// the record — using the same <see cref="ProbationReview.Cancel"/>/<c>SupersededByReviewId</c>
/// pattern PROB-01's <c>ProbationExtensionService</c> already established — and immediately
/// creates fresh reviews (and matching tasks) for the recalculated schedule, mirroring how
/// ProbationExtensionService creates its replacement FinalDecision review immediately rather than
/// waiting for the next daily job run.
///
/// <c>Completed</c> reviews are never touched, satisfying "preserve completed historical
/// reviews" — the query below only ever selects Pending rows. <c>ExtensionConfirmation</c> reviews
/// are also left alone: they are one-off records of a specific extension decision, not part of the
/// recurring checkpoint schedule this service recalculates.
///
/// Idempotency: callers are responsible for only invoking this when the relevant date/setting has
/// actually changed (see UpdateProbationRecordHandler, which compares the previous and new
/// ExpectedEndDate before calling in). Re-running this method with an unchanged schedule is itself
/// safe — it would cancel and recreate identical-looking Pending reviews — but callers avoid that
/// redundant churn by gating on an actual change, the same guard rail PROB-01 documents for
/// ProbationExtensionService.
/// </summary>
internal sealed class ProbationReviewRecalculationService(
    ProbationDbContext dbContext,
    ITaskCreator taskCreator,
    ITaskCanceller taskCanceller,
    IEmployeeNameReader employeeNameReader)
{
    private static readonly ProbationReviewType[] RecalculatedTypes =
    [
        ProbationReviewType.ManagerCheckIn,
        ProbationReviewType.HrReview,
        ProbationReviewType.FinalDecision
    ];

    public async Task RecalculateAsync(
        ProbationRecord record,
        IReadOnlyList<int> checkpointDays,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pendingReviews = await dbContext.ProbationReviews
            .Where(r => r.CompanyId == record.CompanyId
                && r.ProbationRecordId == record.Id
                && r.Status == ProbationReviewStatus.Pending
                && RecalculatedTypes.Contains(r.ReviewType))
            .ToListAsync(cancellationToken);

        var schedule = ProbationReviewScheduler.BuildSchedule(record.StartDate, record.ExpectedEndDate, checkpointDays);

        var newReviews = schedule
            .Select(entry => ProbationReview.Create(
                Guid.NewGuid(), record.CompanyId, record.Id, entry.ReviewType, entry.DueDate, now))
            .ToList();

        // Cancel every still-pending review being replaced. supersededByReviewId points at the
        // fresh review of the same type where one exists (mirrors ProbationExtensionService's
        // FinalDecision supersession); older reviews that were the only one of a now-dropped
        // checkpoint type have no direct replacement and are cancelled with a null reference.
        foreach (var pending in pendingReviews)
        {
            var replacement = newReviews.FirstOrDefault(r => r.ReviewType == pending.ReviewType);
            pending.Cancel(replacement?.Id, now);
            await taskCanceller.CancelBySourceEntityAsync(
                record.CompanyId, pending.Id, TaskSource.Probation, TaskActionType.Review, cancellationToken);
        }

        dbContext.ProbationReviews.AddRange(newReviews);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (newReviews.Count == 0)
            return;

        var names = await employeeNameReader.GetNamesAsync(record.CompanyId, [record.EmployeeId], cancellationToken);
        var employeeName = names.GetValueOrDefault(record.EmployeeId, "Unknown Employee");

        foreach (var review in newReviews)
        {
            await taskCreator.CreateAsync(
                record.CompanyId,
                record.ManagerEmployeeId,
                $"Complete probation review — {employeeName}",
                $"Probation {ReviewTypeLabel(review.ReviewType)} due {review.DueDate:d MMM yyyy} (rescheduled).",
                TaskPriority.High,
                TaskSource.Probation,
                TaskActionType.Review,
                review.DueDate,
                assignedEmployeeId: record.ManagerEmployeeId,
                assignedUserId: record.ManagerEmployeeId,
                sourceEntityId: review.Id,
                cancellationToken,
                notifyAssignee: false);
        }
    }

    private static string ReviewTypeLabel(ProbationReviewType reviewType) => reviewType switch
    {
        ProbationReviewType.ManagerCheckIn => "manager check-in",
        ProbationReviewType.HrReview       => "HR review",
        ProbationReviewType.FinalDecision  => "final decision",
        _                                  => reviewType.ToString()
    };
}
