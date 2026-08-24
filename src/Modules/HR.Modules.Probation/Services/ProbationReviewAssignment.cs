using HR.Modules.Probation.Domain;

namespace HR.Modules.Probation.Services;

/// <summary>
/// PROB-04: pure resolution logic for who a probation review's task/notifications should go to.
///
/// Fixes the bug where every review type (ManagerCheckIn, HrReview, FinalDecision) was assigned to
/// <c>record.ManagerEmployeeId</c> just because that was the only identity readily available —
/// including HrReview, which must never default to the employee's line manager merely because they
/// happen to be the recorded manager.
///
/// - ManagerCheckIn: the employee's current responsible manager (<c>record.ManagerEmployeeId</c>,
///   read fresh at review-creation/recalculation time so manager changes are picked up).
/// - HrReview: an HR queue, resolved via <see cref="HR.Infrastructure.Abstractions.IHrAdministratorDirectory"/>
///   (the existing cross-module HR-audience lookup, already used by PROB-01's extension
///   notifications and by the Support module). The Tasks module's <c>ITaskCreator</c> only supports
///   a single assignee per task, so when multiple HR administrators exist the task itself is
///   assigned to a single, deterministic one (lowest Guid) while every HR administrator still
///   receives the in-app notification — this is a documented limitation, not silent data loss.
/// - FinalDecision / ExtensionConfirmation: the employee's current responsible manager — the
///   manager makes (or confirms) the final call, mirroring how PROB-01's
///   <see cref="ProbationExtensionService"/> already assigns these.
/// </summary>
internal static class ProbationReviewAssignment
{
    /// <summary>
    /// The single employee a review's task should be assigned to, or null if no eligible assignee
    /// exists (e.g. an HrReview with zero configured HR administrators, or a ManagerCheckIn whose
    /// employee currently has no manager).
    /// </summary>
    public static Guid? ResolveTaskAssignee(
        ProbationRecord record, ProbationReviewType reviewType, IReadOnlyList<Guid> hrAdministratorIds)
    {
        if (reviewType == ProbationReviewType.HrReview)
        {
            if (hrAdministratorIds.Count == 0)
                return null;

            return hrAdministratorIds.OrderBy(id => id).First();
        }

        return record.ManagerEmployeeId;
    }

    /// <summary>
    /// Every employee who should receive a "review due" (or similar) notification for this review
    /// type — as opposed to <see cref="ResolveTaskAssignee"/>, which is capped at one recipient by
    /// the Tasks module's single-assignee model, notifications fan out to the full HR queue.
    /// </summary>
    public static IReadOnlyList<Guid> ResolveNotificationRecipients(
        ProbationRecord record, ProbationReviewType reviewType, IReadOnlyList<Guid> hrAdministratorIds)
    {
        if (reviewType == ProbationReviewType.HrReview)
            return hrAdministratorIds;

        return [record.ManagerEmployeeId];
    }
}
