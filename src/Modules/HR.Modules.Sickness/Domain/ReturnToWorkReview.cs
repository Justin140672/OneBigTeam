namespace HR.Modules.Sickness.Domain;

internal sealed class ReturnToWorkReview
{
    private ReturnToWorkReview() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SicknessRecordId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateOnly DueDate { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public string? Notes { get; private set; }
    public ReturnToWorkReviewStatus Status { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ReturnToWorkReview Create(
        Guid id,
        Guid companyId,
        Guid sicknessRecordId,
        Guid employeeId,
        DateOnly dueDate,
        DateTimeOffset now)
    {
        return new ReturnToWorkReview
        {
            Id = id,
            CompanyId = companyId,
            SicknessRecordId = sicknessRecordId,
            EmployeeId = employeeId,
            DueDate = dueDate,
            Status = ReturnToWorkReviewStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Complete(Guid reviewedBy, string? notes, DateTimeOffset now)
    {
        Status = ReturnToWorkReviewStatus.Completed;
        ReviewedBy = reviewedBy;
        Notes = notes;
        CompletedAt = now;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        Status = ReturnToWorkReviewStatus.Cancelled;
        UpdatedAt = now;
    }

    public void MarkOverdue(DateTimeOffset now)
    {
        Status = ReturnToWorkReviewStatus.Overdue;
        UpdatedAt = now;
    }
}
