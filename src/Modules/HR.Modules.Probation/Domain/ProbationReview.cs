namespace HR.Modules.Probation.Domain;

internal sealed class ProbationReview
{
    private ProbationReview() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ProbationRecordId { get; private set; }
    public ProbationReviewType ReviewType { get; private set; }
    public DateOnly DueDate { get; private set; }
    public ProbationReviewStatus Status { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid? CompletedByEmployeeId { get; private set; }
    public ProbationOutcome? Outcome { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ProbationReview Create(
        Guid id,
        Guid companyId,
        Guid probationRecordId,
        ProbationReviewType reviewType,
        DateOnly dueDate,
        DateTimeOffset now)
    {
        return new ProbationReview
        {
            Id = id,
            CompanyId = companyId,
            ProbationRecordId = probationRecordId,
            ReviewType = reviewType,
            DueDate = dueDate,
            Status = ProbationReviewStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Complete(Guid completedByEmployeeId, ProbationOutcome? outcome, string? notes, DateTimeOffset now)
    {
        Status = ProbationReviewStatus.Completed;
        CompletedAt = now;
        CompletedByEmployeeId = completedByEmployeeId;
        Outcome = outcome;
        Notes = notes;
        UpdatedAt = now;
    }
}
