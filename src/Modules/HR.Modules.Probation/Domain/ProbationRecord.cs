namespace HR.Modules.Probation.Domain;

internal sealed class ProbationRecord
{
    /// <summary>
    /// PROB-05: explicit allowed-transition table for <see cref="ProbationStatus"/>. Passed and
    /// Failed are terminal — no further transitions are permitted once reached. Extended is
    /// non-terminal: an extended record can still go on to a further extension, a review-due
    /// state, or a final Pass/Fail decision. Every status-changing domain method
    /// (<see cref="MarkReviewDue"/>, <see cref="Extend"/>, <see cref="Pass"/>, <see cref="Fail"/>)
    /// must call <see cref="AssertCanTransitionTo"/> before mutating <see cref="Status"/> so an
    /// invalid transition can never reach persistence, regardless of caller.
    /// </summary>
    private static readonly IReadOnlyDictionary<ProbationStatus, ProbationStatus[]> AllowedTransitions =
        new Dictionary<ProbationStatus, ProbationStatus[]>
        {
            [ProbationStatus.Active] =
            [
                ProbationStatus.ReviewDue, ProbationStatus.Extended, ProbationStatus.Passed, ProbationStatus.Failed
            ],
            [ProbationStatus.ReviewDue] =
            [
                ProbationStatus.Extended, ProbationStatus.Passed, ProbationStatus.Failed
            ],
            [ProbationStatus.Extended] =
            [
                ProbationStatus.ReviewDue, ProbationStatus.Extended, ProbationStatus.Passed, ProbationStatus.Failed
            ],
            [ProbationStatus.Passed] = [],
            [ProbationStatus.Failed] = []
        };

    private ProbationRecord() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid ManagerEmployeeId { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly ExpectedEndDate { get; private set; }
    public ProbationStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public string? ExtensionReason { get; private set; }
    public DateOnly? DecisionDate { get; private set; }
    public Guid? DecisionMakerEmployeeId { get; private set; }
    public string? OutcomeNotes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ProbationRecord Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid managerEmployeeId,
        DateOnly startDate,
        DateOnly expectedEndDate,
        string? notes,
        DateTimeOffset now)
    {
        return new ProbationRecord
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            ManagerEmployeeId = managerEmployeeId,
            StartDate = startDate,
            ExpectedEndDate = expectedEndDate,
            Status = ProbationStatus.Active,
            Notes = notes,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// PROB-05: the only supported "direct edit" path — an administrative correction of fields
    /// that never encode a workflow decision (assigned manager, expected end date typo/correction,
    /// free-text notes). Deliberately does NOT accept <see cref="Status"/>, <see cref="ExtensionReason"/>,
    /// <see cref="DecisionMakerEmployeeId"/>, <see cref="DecisionDate"/> or <see cref="OutcomeNotes"/> —
    /// those fields may only be set together, consistently, by <see cref="Extend"/>, <see cref="Pass"/>
    /// or <see cref="Fail"/> as part of the proper review-completion/extension workflow. Allowing a
    /// direct setter for Status (or the outcome fields that must agree with it) would let a caller
    /// create an internally inconsistent record — e.g. Status=Passed with no DecisionDate, or
    /// Status=Active with stale outcome fields from a prior decision.
    /// Not permitted once the record has reached a terminal state (Passed/Failed) — a terminal
    /// outcome is a completed decision and must not be edited after the fact.
    /// </summary>
    public void ApplyAdministrativeCorrection(
        Guid managerEmployeeId,
        DateOnly expectedEndDate,
        string? notes,
        DateTimeOffset now)
    {
        if (Status is ProbationStatus.Passed or ProbationStatus.Failed)
            throw new InvalidOperationException(
                $"Cannot edit a probation record that has already reached the terminal status '{Status}'.");

        ManagerEmployeeId = managerEmployeeId;
        ExpectedEndDate = expectedEndDate;
        Notes = notes;
        UpdatedAt = now;
    }

    private void AssertCanTransitionTo(ProbationStatus newStatus)
    {
        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(newStatus))
            throw new InvalidOperationException(
                $"Cannot transition probation record from '{Status}' to '{newStatus}'.");
    }

    public void MarkReviewDue(DateTimeOffset now)
    {
        AssertCanTransitionTo(ProbationStatus.ReviewDue);
        Status = ProbationStatus.ReviewDue;
        UpdatedAt = now;
    }

    /// <summary>
    /// PROB-04: applies a manager change originating from the Employees module
    /// (<c>EmployeeManagerChangedIntegrationEvent</c>) so that ManagerCheckIn/FinalDecision/
    /// ExtensionConfirmation tasks created after this point resolve to the employee's current
    /// responsible manager rather than whoever was recorded at probation-record creation time.
    /// </summary>
    public void ChangeManager(Guid newManagerEmployeeId, DateTimeOffset now)
    {
        ManagerEmployeeId = newManagerEmployeeId;
        UpdatedAt = now;
    }

    public void Extend(
        DateOnly newExpectedEndDate,
        string extensionReason,
        Guid decisionMakerEmployeeId,
        DateOnly decisionDate,
        DateTimeOffset now)
    {
        AssertCanTransitionTo(ProbationStatus.Extended);

        // PROB-05: an extension must move the expected end date forward — never sideways,
        // backwards, or only relative to "today". Both comparisons are required: against the
        // record's current ExpectedEndDate (an extension that doesn't actually extend is
        // meaningless) and against the decision date itself (an extension can't be backdated to
        // end before/on the day the decision was made).
        if (newExpectedEndDate <= ExpectedEndDate)
            throw new InvalidOperationException(
                "New expected end date must be later than the current expected end date.");

        if (newExpectedEndDate <= decisionDate)
            throw new InvalidOperationException(
                "New expected end date must be later than the decision date.");

        ExpectedEndDate = newExpectedEndDate;
        ExtensionReason = extensionReason;
        DecisionMakerEmployeeId = decisionMakerEmployeeId;
        DecisionDate = decisionDate;
        Status = ProbationStatus.Extended;
        UpdatedAt = now;
    }

    public void Pass(
        Guid decisionMakerEmployeeId,
        DateOnly decisionDate,
        string? outcomeNotes,
        DateTimeOffset now)
    {
        AssertCanTransitionTo(ProbationStatus.Passed);

        DecisionMakerEmployeeId = decisionMakerEmployeeId;
        DecisionDate = decisionDate;
        OutcomeNotes = outcomeNotes;
        Status = ProbationStatus.Passed;
        UpdatedAt = now;
    }

    public void Fail(
        Guid decisionMakerEmployeeId,
        DateOnly decisionDate,
        string? outcomeNotes,
        DateTimeOffset now)
    {
        AssertCanTransitionTo(ProbationStatus.Failed);

        DecisionMakerEmployeeId = decisionMakerEmployeeId;
        DecisionDate = decisionDate;
        OutcomeNotes = outcomeNotes;
        Status = ProbationStatus.Failed;
        UpdatedAt = now;
    }
}
