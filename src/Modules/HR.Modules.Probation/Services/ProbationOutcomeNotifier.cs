using HR.Modules.Probation.Domain;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Probation.Services;

/// <summary>
/// PROB-04: notifies the employee when their FinalDecision review is completed with a Pass or Fail
/// outcome. Shared by both completion paths (the direct API handler
/// <c>CompleteProbationReviewHandler</c> and the task-driven
/// <c>CompleteProbationReviewFromTaskAction</c>) so the notification behaves identically regardless
/// of how the review was completed.
///
/// Notification text is limited to safe summary fields — review type, outcome and decision date —
/// and deliberately never includes <see cref="ProbationReview.Notes"/> or
/// <see cref="ProbationRecord"/>'s free-text fields (OutcomeNotes, ExtensionReason), which may
/// contain a manager's private commentary about the employee. This mirrors the sensitive-data
/// exclusion already applied elsewhere in this codebase (e.g. SICK-06) to keep manager-authored
/// free text out of employee-facing notifications.
///
/// Idempotent against duplicate execution (e.g. a retried task-completion action) via
/// <see cref="INotificationWriter.ExistsAsync"/> keyed on (employee, review id, notification type).
/// </summary>
internal static class ProbationOutcomeNotifier
{
    public static async Task NotifyAsync(
        INotificationWriter notificationWriter,
        ProbationRecord record,
        ProbationReview review,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var alreadySent = await notificationWriter.ExistsAsync(
            record.EmployeeId, review.Id, NotificationType.ProbationOutcomeRecorded, cancellationToken);

        if (alreadySent)
            return;

        var outcomeLabel = review.Outcome switch
        {
            ProbationOutcome.Pass => "passed",
            ProbationOutcome.Fail => "not passed",
            _                     => "recorded"
        };

        var title = "Probation outcome recorded";
        var body = $"Your {ReviewTypeLabel(review.ReviewType)} outcome has been {outcomeLabel} " +
                   $"(decision date {record.DecisionDate:d MMM yyyy}).";

        await notificationWriter.WriteAsync(
            Guid.NewGuid(),
            record.CompanyId,
            record.EmployeeId,
            title,
            body,
            review.Id,
            NotificationType.ProbationOutcomeRecorded,
            NotificationPriority.High,
            now,
            cancellationToken);
    }

    private static string ReviewTypeLabel(ProbationReviewType reviewType) => reviewType switch
    {
        ProbationReviewType.ManagerCheckIn => "manager check-in",
        ProbationReviewType.HrReview       => "HR review",
        ProbationReviewType.FinalDecision  => "final decision",
        _                                  => reviewType.ToString()
    };
}
