namespace HR.Infrastructure.Abstractions;

public enum NotificationType
{
    TaskAssigned     = 1,
    TaskDueSoon      = 2,
    TaskOverdue      = 3,
    LeaveApproved    = 4,
    LeaveRejected    = 5,
    LeaveRequested   = 6,
    DocumentExpiring = 7,
    DocumentExpired  = 8,
    TaskCompleted         = 9,
    AssetAssigned         = 10,
    AssetReturnRequested  = 11,
    AssetAcknowledgementReminder = 12,
    AssetReturnReminder          = 13,
    AssetAcknowledgementOverdue  = 14,
    AssetReturnOverdue           = 15,
    SicknessRecorded             = 16,
    SicknessEvidenceReminder     = 17,
    SicknessEvidenceOverdue      = 18,
    ReturnToWorkReviewReminder   = 19,
    ReturnToWorkReviewOverdue    = 20,
    InterviewScheduled           = 21,
    InterviewFeedbackOverdue     = 22,
    InterviewReminder            = 23,
    OnboardingStarted            = 24,
    OnboardingTaskOverdue        = 25,
    OffboardingStarted           = 26,
    OffboardingTaskOverdue       = 27,
    OffboardingCompleted         = 28,
    ProfilePhotoApproved         = 29,
    ProfilePhotoRejected         = 30,
    SharedCompanyDocumentAcknowledgementReminder = 31,
    SharedCompanyDocumentAcknowledgementOverdue  = 32,
    SharedCompanyDocumentReviewDue                = 33,
    SharedCompanyDocumentManagerEscalation        = 34,
    LeavingProcessStarted                         = 35,
    IncompleteOffboardingAtDeparture              = 36,
    SupportRequestStatusChanged                   = 37,
    ProbationExtended                             = 38,
    ProbationReviewDue                            = 39,
    ProbationOutcomeRecorded                      = 40,
    // OFF-02: raised when an outstanding task's due date is shifted as a side effect of an
    // amendment to the underlying business date it was derived from (e.g. an employee's last
    // working day) — distinct from TaskAssigned/TaskDueSoon/TaskOverdue, none of which fit a
    // date-only change to a task the assignee already knows about.
    TaskDateChanged                               = 41,
    // OFF-05: raised at offboarding-plan creation time for a backdated departure that generated at
    // least one HR reconciliation task (outstanding assets/documents/access the departed employee
    // can no longer action themselves). Sent to HR administrators, distinct from
    // IncompleteOffboardingAtDeparture (which fires later, to the manager, at departure
    // finalisation if offboarding is still incomplete) and from OffboardingStarted (the routine
    // "plan created" notice).
    OffboardingRequiresHrReconciliation           = 42,
}
