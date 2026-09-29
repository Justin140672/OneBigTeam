using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Domain;

/// <summary>
/// NOT-04: per-NotificationType navigation target. Computes the application-relative URL a
/// notification's click/keyboard activation should navigate to, given the type plus the
/// identifiers already available at every INotificationWriter call site (companyId, employeeId,
/// sourceEntityId). Computed once by NotificationWriter at write time and persisted on
/// Notification.ActionUrl — never recomputed per read.
///
/// Types with no natural destination (purely informational, or referring to a screen that does
/// not exist in HR.Web today — see the Interview* and SupportRequestStatusChanged comments below)
/// return null. A null ActionUrl means the notification is display-only; HR.Web must not fall back
/// to any other navigation behaviour when it sees null (see NOT-04 acceptance criteria).
///
/// Every branch below returns either null or a value that already satisfies EnforceRelative's
/// invariant (starts with a single '/', never "http://", "https://" or "//"). EnforceRelative is
/// still applied to the final result as a defensive, non-bypassable guard: because every input to
/// this builder is a computed identifier (Guid) rather than user input, EnforceRelative should
/// never actually reject anything in practice, but it documents and enforces the invariant this
/// class exists to guarantee, per NOT-04's explicit "external and unsafe URLs cannot be stored or
/// followed" acceptance criterion.
/// </summary>
internal static class NotificationActionRouteBuilder
{
    public static string? BuildActionUrl(
        NotificationType type, Guid companyId, Guid employeeId, Guid sourceEntityId)
    {
        var url = type switch
        {
            NotificationType.TaskAssigned
                or NotificationType.TaskDueSoon
                or NotificationType.TaskOverdue
                or NotificationType.TaskCompleted
                or NotificationType.TaskDateChanged
                => $"/companies/{companyId}/tasks/{sourceEntityId}",

            NotificationType.LeaveApproved
                or NotificationType.LeaveRejected
                or NotificationType.LeaveRequested
                => $"/companies/{companyId}/employees/{employeeId}?tab=leave",

            NotificationType.DocumentExpiring
                or NotificationType.DocumentExpired
                => $"/companies/{companyId}/employees/{employeeId}?tab=documents",

            NotificationType.AssetAssigned
                or NotificationType.AssetReturnRequested
                or NotificationType.AssetAcknowledgementReminder
                or NotificationType.AssetReturnReminder
                or NotificationType.AssetAcknowledgementOverdue
                or NotificationType.AssetReturnOverdue
                => $"/companies/{companyId}/assets/{sourceEntityId}/view",

            NotificationType.SicknessRecorded
                or NotificationType.SicknessEvidenceReminder
                or NotificationType.SicknessEvidenceOverdue
                or NotificationType.ReturnToWorkReviewReminder
                or NotificationType.ReturnToWorkReviewOverdue
                => $"/companies/{companyId}/employees/{employeeId}?tab=sickness",

            // Recruitment interview notifications intentionally have no destination. SourceEntityId
            // for every Interview* type is the interview id, but HR.Web has no interview-detail
            // route today (interviews are only ever shown inline on the vacancy kanban board /
            // candidate detail page, both of which require a vacancy or candidate id this module
            // never receives at write time). Rather than guess a wrong destination, these remain
            // informational only until a real interview route exists — a known limitation, not an
            // oversight.
            NotificationType.InterviewScheduled
                or NotificationType.InterviewFeedbackOverdue
                or NotificationType.InterviewReminder
                => null,

            NotificationType.OnboardingStarted
                or NotificationType.OnboardingTaskOverdue
                => $"/companies/{companyId}/employees/{employeeId}?tab=onboarding",

            NotificationType.OffboardingStarted
                or NotificationType.OffboardingTaskOverdue
                or NotificationType.OffboardingCompleted
                or NotificationType.OffboardingRequiresHrReconciliation
                => $"/companies/{companyId}/employees/{employeeId}?tab=offboarding",

            NotificationType.LeavingProcessStarted
                or NotificationType.IncompleteOffboardingAtDeparture
                => $"/companies/{companyId}/employees/{employeeId}?tab=leaving",

            NotificationType.ProfilePhotoApproved
                or NotificationType.ProfilePhotoRejected
                => $"/companies/{companyId}/employees/{employeeId}/profile",

            NotificationType.SharedCompanyDocumentAcknowledgementReminder
                or NotificationType.SharedCompanyDocumentAcknowledgementOverdue
                or NotificationType.SharedCompanyDocumentReviewDue
                or NotificationType.SharedCompanyDocumentManagerEscalation
                => $"/companies/{companyId}/shared-documents/{sourceEntityId}",

            NotificationType.SupportRequestStatusChanged
                => $"/companies/{companyId}/support/{sourceEntityId}",

            NotificationType.ProbationExtended
                or NotificationType.ProbationReviewDue
                or NotificationType.ProbationOutcomeRecorded
                => $"/companies/{companyId}/employees/{employeeId}?tab=probation",

            NotificationType.EmployeeCreated
                => $"/companies/{companyId}/employees/{sourceEntityId}",
            NotificationType.CandidateHired
                => $"/companies/{companyId}/candidates/{sourceEntityId}",

            NotificationType.OrganisationDataExportReady
                => "/subscription",

            _ => null,
        };

        return EnforceRelative(url);
    }

    public static string? EnforceRelative(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        if (!url.StartsWith('/') || url.StartsWith("//"))
            return null;

        return url;
    }
}
