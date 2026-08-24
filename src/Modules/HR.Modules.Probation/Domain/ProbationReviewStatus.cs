namespace HR.Modules.Probation.Domain;

internal enum ProbationReviewStatus
{
    Pending = 1,
    Completed = 2,

    /// <summary>
    /// The review was made obsolete before completion — e.g. a pending FinalDecision review
    /// whose due date no longer applies because probation was extended and a replacement
    /// FinalDecision review was scheduled for the new expected end date. Retained for audit
    /// traceability rather than deleted.
    /// </summary>
    Cancelled = 3
}
