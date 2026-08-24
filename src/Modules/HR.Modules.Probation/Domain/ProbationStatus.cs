namespace HR.Modules.Probation.Domain;

internal enum ProbationStatus
{
    Active = 1,
    ReviewDue = 2,
    Extended = 3,
    Passed = 4,
    Failed = 5,

    /// <summary>
    /// PROB-06: the employee's probation start date is in the future. Stored (not derived) so it
    /// can be queried/filtered like any other status; transitions to <see cref="Active"/> via
    /// <see cref="ProbationRecord.ActivateIfDue"/>, called from the existing daily
    /// GenerateDueProbationReviewsJob rather than at arbitrary read time — see that job for the
    /// rationale.
    /// </summary>
    NotStarted = 6,

    /// <summary>
    /// PROB-06: an explicit decision that probation does not apply to this employee (e.g. their
    /// role/employment type is exempt). Terminal — no further transitions, and — because
    /// GenerateDueProbationReviewsJob only ever queries Active/ReviewDue records — no reviews are
    /// ever generated for a record in this status.
    /// </summary>
    NotApplicable = 7
}
