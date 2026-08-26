using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Domain;

/// <summary>
/// Per-NotificationType default delivery channel (NOT-02). A per-type lookup was chosen over
/// threading an explicit channel parameter through every INotificationWriter.WriteAsync call site
/// (there are 30+ across nearly every module) — this keeps every existing call site unchanged and
/// centralises the "does this type warrant email" decision in one place, which is far easier to
/// audit and keep consistent than 30+ call-site decisions.
///
/// Deliberately conservative: only notification types that are either (a) a significant/urgent
/// business outcome the recipient should learn about even if they never open the app, or
/// (b) time-sensitive enough that an in-app-only notification risks being missed, are given Email
/// in addition to InApp. High-frequency/routine types (TaskAssigned, TaskDueSoon, reminders that
/// already recur, etc.) stay InApp-only to avoid turning email into a second, noisier notification
/// feed. TaskDateChanged was considered (raised by NOT-02) but rejected as an email default — it is
/// a routine, frequent, low-stakes reschedule notice, not something that warrants an interruption
/// outside the app.
/// </summary>
internal static class NotificationChannelDefaults
{
    private static readonly HashSet<NotificationType> EmailEligibleTypes =
    [
        // Leave decisions materially affect the employee's plans and are infrequent enough that
        // email is proportionate.
        NotificationType.LeaveApproved,
        NotificationType.LeaveRejected,

        // A recorded probation outcome and a review coming due are both significant/time-sensitive
        // career events.
        NotificationType.ProbationOutcomeRecorded,
        NotificationType.ProbationReviewDue,

        // Both offboarding types below are urgent, rare, action-required alerts to HR/managers
        // that must not depend on someone happening to check in-app notifications.
        NotificationType.OffboardingRequiresHrReconciliation,
        NotificationType.IncompleteOffboardingAtDeparture,

        // Compliance-relevant overdue/expiry states — missing these has real consequences.
        NotificationType.DocumentExpired,
        NotificationType.SicknessEvidenceOverdue,
        NotificationType.ReturnToWorkReviewOverdue,
    ];

    public static NotificationChannel GetChannel(NotificationType type) =>
        EmailEligibleTypes.Contains(type) ? NotificationChannel.Both : NotificationChannel.InApp;

    // SET-06: the subset of EmailEligibleTypes that are mandatory/non-optional security or
    // compliance communications — the company's EmailNotificationsEnabled=false setting never
    // suppresses these (see NotificationWriter/EmailDeliveryJob). Deliberately the same three
    // "compliance-relevant overdue/expiry states" already called out in the class-level remarks
    // above; every other EmailEligibleType is optional and respects the company's setting.
    private static readonly HashSet<NotificationType> MandatoryEmailTypes =
    [
        NotificationType.DocumentExpired,
        NotificationType.SicknessEvidenceOverdue,
        NotificationType.ReturnToWorkReviewOverdue,
    ];

    public static bool IsMandatoryEmail(NotificationType type) => MandatoryEmailTypes.Contains(type);

    // SET-06: notification types raised by a recurring/scheduled background job rather than
    // directly by a user action, gated separately by the company's ScheduledRemindersEnabled
    // setting (in-app AND email both suppressed when off — distinct from EmailNotificationsEnabled,
    // which only ever gates the email channel). Overdue/escalation types (TaskOverdue,
    // DocumentExpired, etc.) are deliberately NOT included here even though they too originate from
    // scheduled jobs — they represent an already-missed deadline, a materially different
    // "something needs to happen now" alert rather than a proactive advance reminder, so they
    // remain unaffected by this setting.
    private static readonly HashSet<NotificationType> ScheduledReminderTypes =
    [
        NotificationType.TaskDueSoon,
        NotificationType.DocumentExpiring,
        NotificationType.AssetAcknowledgementReminder,
        NotificationType.AssetReturnReminder,
        NotificationType.SicknessEvidenceReminder,
        NotificationType.ReturnToWorkReviewReminder,
        NotificationType.InterviewReminder,
        NotificationType.SharedCompanyDocumentAcknowledgementReminder,
    ];

    public static bool IsScheduledReminder(NotificationType type) => ScheduledReminderTypes.Contains(type);
}
