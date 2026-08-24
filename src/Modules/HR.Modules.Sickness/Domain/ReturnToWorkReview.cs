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

    /// <summary>
    /// Manager/HR free-text notes on the review. May contain sensitive return-to-work detail —
    /// trimmed from non-HR callers by <see cref="Services.SicknessResourceAuthorizer"/> consumers
    /// (see GetReturnToWorkReview, SICK-02).
    /// </summary>
    public string? Notes { get; private set; }

    /// <summary>
    /// SICK-03: the structured fit-to-return decision. Null until the review is completed —
    /// completion is not permitted without a value (enforced by
    /// Features/CompleteReturnToWorkReview/Validator.cs).
    /// </summary>
    public FitToReturnOutcome? Outcome { get; private set; }

    /// <summary>Whether workplace adjustments are required for the employee to return.</summary>
    public bool AdjustmentsRequired { get; private set; }

    /// <summary>
    /// Free-text description of the suitable workplace adjustments required (e.g. phased return,
    /// altered hours, amended duties, equipment). Intentionally scoped to *adjustments*, not
    /// medical diagnosis — the field name and UI copy should steer managers/HR away from
    /// recording clinical detail here; no content filtering is implemented, this is a process/
    /// UI convention rather than a technical control.
    /// </summary>
    public string? AdjustmentDetails { get; private set; }

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

    /// <summary>
    /// Records the structured review outcome and marks the review Completed. Idempotent: a
    /// review that is already Completed is left entirely unchanged (SICK-03 — "repeated task
    /// completion does not overwrite or duplicate the review"), so callers may invoke this
    /// safely from more than one code path (e.g. both the dedicated CompleteReturnToWorkReview
    /// endpoint and a defensive re-dispatch from the Tasks module) without risk of overwriting
    /// an already-recorded outcome or re-firing audit events.
    /// </summary>
    public void Complete(
        Guid reviewedBy,
        FitToReturnOutcome outcome,
        bool adjustmentsRequired,
        string? adjustmentDetails,
        string? notes,
        DateTimeOffset now)
    {
        if (Status == ReturnToWorkReviewStatus.Completed)
            return;

        Status = ReturnToWorkReviewStatus.Completed;
        ReviewedBy = reviewedBy;
        Outcome = outcome;
        AdjustmentsRequired = adjustmentsRequired;
        AdjustmentDetails = adjustmentDetails;
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
