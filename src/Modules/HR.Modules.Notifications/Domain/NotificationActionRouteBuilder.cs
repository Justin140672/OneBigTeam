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
            // Tasks module owns a single task-detail route keyed by task id, which is exactly
            // SourceEntityId for every task-related notification type.
            NotificationType.TaskAssigned
                or NotificationType.TaskDueSoon
                or NotificationType.TaskOverdue
                or NotificationType.TaskCompleted
                or NotificationType.TaskDateChanged
                => $"/companies/{companyId}/tasks/{sourceEntityId}",

            // Leave has no standalone request-detail route; leave requests are reviewed from the
            // employee record's Leave tab.
            NotificationType.LeaveApproved
                or NotificationType.LeaveRejected
                or NotificationType.LeaveRequested
                => $"/companies/{companyId}/employees/{employeeId}?tab=leave",

            // Documents (employee-owned expiring/expired documents) are reviewed from the
            // employee record's Documents tab — there is no standalone document-detail route.
            NotificationType.DocumentExpiring
                or NotificationType.DocumentExpired
                => $"/companies/{companyId}/employees/{employeeId}?tab=documents",

            // Assets module owns a single asset-detail route keyed by asset id. Every asset
            // notification's SourceEntityId is the assignment id, not the asset id, but the
            // assignment is shown inline on the asset detail page, which is the closest and most
            // useful landing point available.
            NotificationType.AssetAssigned
                or NotificationType.AssetReturnRequested
                or NotificationType.AssetAcknowledgementReminder
                or NotificationType.AssetReturnReminder
                or NotificationType.AssetAcknowledgementOverdue
                or NotificationType.AssetReturnOverdue
                => $"/companies/{companyId}/assets/{sourceEntityId}/view",

            // Sickness has no standalone record-detail route; sickness records and
            // return-to-work reviews are reviewed from the employee record's Sickness tab.
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

            // Onboarding/offboarding progress is reviewed from the employee record's Onboarding /
            // Offboarding tabs; there is no standalone plan-detail route.
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

            // Profile photo review always lands the employee back on their own profile page.
            NotificationType.ProfilePhotoApproved
                or NotificationType.ProfilePhotoRejected
                => $"/companies/{companyId}/employees/{employeeId}/profile",

            // Shared company documents have a single detail route keyed by document id, which is
            // exactly SourceEntityId for every one of these notification types.
            NotificationType.SharedCompanyDocumentAcknowledgementReminder
                or NotificationType.SharedCompanyDocumentAcknowledgementOverdue
                or NotificationType.SharedCompanyDocumentReviewDue
                or NotificationType.SharedCompanyDocumentManagerEscalation
                => $"/companies/{companyId}/shared-documents/{sourceEntityId}",

            // Support requests are viewed via SearchPageBase's established "/support/{id}"
            // deep-link convention (see SupportRequestQueue.GetViewUrl), keyed by the support
            // request id, which is exactly SourceEntityId here.
            NotificationType.SupportRequestStatusChanged
                => $"/companies/{companyId}/support/{sourceEntityId}",

            // Probation events are reviewed from the employee record's Probation tab; there is no
            // standalone probation-detail route.
            NotificationType.ProbationExtended
                or NotificationType.ProbationReviewDue
                or NotificationType.ProbationOutcomeRecorded
                => $"/companies/{companyId}/employees/{employeeId}?tab=probation",

            // NOT-03 template-catalogue types with no live call site yet, included here for
            // completeness. SourceEntityId is the employee/candidate id respectively.
            NotificationType.EmployeeCreated
                => $"/companies/{companyId}/employees/{sourceEntityId}",
            NotificationType.CandidateHired
                => $"/companies/{companyId}/candidates/{sourceEntityId}",

            _ => null,
        };

        return EnforceRelative(url);
    }

    /// <summary>
    /// Defensive guard documenting the "application-relative only" invariant every branch above
    /// must already satisfy. Rejects (returns null instead of) anything that isn't a same-origin
    /// relative path — absolute URLs ("http://…", "https://…") and scheme-relative URLs ("//…")
    /// are never stored or returned, per NOT-04's external/unsafe URL acceptance criterion. This
    /// should never trigger given every branch above is a hard-coded relative template, but it is
    /// the single choke point every computed URL passes through before reaching persistence.
    /// </summary>
    private static string? EnforceRelative(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        if (!url.StartsWith('/') || url.StartsWith("//"))
            return null;

        return url;
    }
}
