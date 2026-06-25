namespace HR.Modules.Probation.Domain;

internal sealed class ProbationRecord
{
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

    public void MarkReviewDue(DateTimeOffset now)
    {
        Status = ProbationStatus.ReviewDue;
        UpdatedAt = now;
    }

    public void Extend(
        DateOnly newExpectedEndDate,
        string extensionReason,
        Guid decisionMakerEmployeeId,
        DateOnly decisionDate,
        DateTimeOffset now)
    {
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
        DecisionMakerEmployeeId = decisionMakerEmployeeId;
        DecisionDate = decisionDate;
        OutcomeNotes = outcomeNotes;
        Status = ProbationStatus.Failed;
        UpdatedAt = now;
    }
}
