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

    /// <summary>
    /// Set when this review is cancelled because it was superseded by a replacement review
    /// (e.g. a FinalDecision review rescheduled to a new expected end date after an extension).
    /// Null for reviews that were never superseded.
    /// </summary>
    public Guid? SupersededByReviewId { get; private set; }
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

    /// <summary>
    /// Cancels a still-pending review because it has been superseded by a replacement review
    /// (e.g. probation extended, so the original FinalDecision due date no longer applies).
    /// No-op if the review is already Cancelled or Completed, so callers can call this
    /// unconditionally without checking status first.
    /// </summary>
    public void Cancel(Guid? supersededByReviewId, DateTimeOffset now)
    {
        if (Status != ProbationReviewStatus.Pending)
            return;

        Status = ProbationReviewStatus.Cancelled;
        SupersededByReviewId = supersededByReviewId;
        UpdatedAt = now;
    }
}
