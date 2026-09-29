using HR.SharedKernel;

namespace HR.Modules.Probation.Domain;

internal sealed class ProbationRecord : IVersionedAggregate
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
    /// <summary>
    /// PROB-06: added NotStarted and NotApplicable. NotStarted can move to Active (start date
    /// reached — see <see cref="ActivateIfDue"/>) or straight to NotApplicable (an applicability
    /// decision made before probation would otherwise have kicked in). Active can also move to
    /// NotApplicable — an early opt-out decision made shortly after the record starts (e.g. a
    /// role/employment-type correction discovered right after creation). ReviewDue/Extended
    /// deliberately cannot move to NotApplicable: by that point real review activity has already
    /// occurred, so "this never applied" is no longer a truthful decision — use Pass/Fail instead.
    /// NotApplicable is terminal, same as Passed/Failed.
    /// </summary>
    private static readonly IReadOnlyDictionary<ProbationStatus, ProbationStatus[]> AllowedTransitions =
        new Dictionary<ProbationStatus, ProbationStatus[]>
        {
            [ProbationStatus.NotStarted] =
            [
                ProbationStatus.Active, ProbationStatus.NotApplicable
            ],
            [ProbationStatus.Active] =
            [
                ProbationStatus.ReviewDue, ProbationStatus.Extended, ProbationStatus.Passed, ProbationStatus.Failed,
                ProbationStatus.NotApplicable
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
            [ProbationStatus.Failed] = [],
            [ProbationStatus.NotApplicable] = []
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
    public string? NotApplicableReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? ManagerChangeSourceOccurredAt { get; private set; }

    // Ticket 16 (optimistic concurrency): explicit, persisted concurrency token. Mapped as an EF
    // concurrency token in ProbationRecordConfiguration. Every other mutation path in this module
    // (CompleteProbationReview, MarkProbationNotApplicable, ReassignReviewsOnManagerChanged,
    // GenerateDueProbationReviewsJob) still calls plain SaveChangesAsync rather than the guarded
    // helper — those writers rely on the shared VersionAdvancingSaveChangesInterceptor (wired via
    // ProbationDbContext's UseVersionedAggregates() call) to advance Version automatically, which
    // satisfies the "must at least advance the version" requirement for internal/system-triggered
    // transitions without requiring a client-supplied ExpectedVersion.
    public int Version { get; private set; } = 1;

    public void IncrementVersion() => Version++;

    public static ProbationRecord Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid managerEmployeeId,
        DateOnly startDate,
        DateOnly expectedEndDate,
        string? notes,
        DateOnly today,
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
            Status = today < startDate ? ProbationStatus.NotStarted : ProbationStatus.Active,
            Notes = notes,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static ProbationRecord CreateNotApplicable(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid managerEmployeeId,
        DateOnly startDate,
        DateOnly expectedEndDate,
        string? reason,
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
            Status = ProbationStatus.NotApplicable,
            NotApplicableReason = reason,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void ActivateIfDue(DateOnly today, DateTimeOffset now)
    {
        if (Status != ProbationStatus.NotStarted || today < StartDate)
            return;

        AssertCanTransitionTo(ProbationStatus.Active);
        Status = ProbationStatus.Active;
        UpdatedAt = now;
    }

    public void MarkNotApplicable(string? reason, DateTimeOffset now)
    {
        AssertCanTransitionTo(ProbationStatus.NotApplicable);
        Status = ProbationStatus.NotApplicable;
        NotApplicableReason = reason;
        UpdatedAt = now;
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
        if (Status is ProbationStatus.Passed or ProbationStatus.Failed or ProbationStatus.NotApplicable)
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

    public void ChangeManager(Guid newManagerEmployeeId, DateTimeOffset now)
    {
        ManagerEmployeeId = newManagerEmployeeId;
        UpdatedAt = now;
    }

    public bool ApplyManagerChangeFromEvent(Guid newManagerEmployeeId, DateTimeOffset occurredAt, DateTimeOffset now)
    {
        if (ManagerChangeSourceOccurredAt is not null && occurredAt < ManagerChangeSourceOccurredAt)
            return false;

        ManagerChangeSourceOccurredAt = occurredAt;

        if (ManagerEmployeeId == newManagerEmployeeId)
            return false;

        ManagerEmployeeId = newManagerEmployeeId;
        UpdatedAt = now;
        return true;
    }

    public void Extend(
        DateOnly newExpectedEndDate,
        string extensionReason,
        Guid decisionMakerEmployeeId,
        DateOnly decisionDate,
        DateTimeOffset now)
    {
        AssertCanTransitionTo(ProbationStatus.Extended);

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
