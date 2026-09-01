namespace HR.Modules.Sickness.Domain;

internal sealed class SicknessEvidenceRequest
{
    private SicknessEvidenceRequest() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SicknessRecordId { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public Guid RequestedBy { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string? Notes { get; private set; }
    public SicknessEvidenceRequestStatus Status { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static SicknessEvidenceRequest Create(
        Guid id,
        Guid companyId,
        Guid sicknessRecordId,
        Guid requestedBy,
        DateOnly dueDate,
        string? notes,
        DateTimeOffset now)
    {
        return new SicknessEvidenceRequest
        {
            Id = id,
            CompanyId = companyId,
            SicknessRecordId = sicknessRecordId,
            RequestedBy = requestedBy,
            DueDate = dueDate,
            Notes = notes,
            Status = SicknessEvidenceRequestStatus.Pending,
            RequestedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Fulfil(DateTimeOffset now)
    {
        Status = SicknessEvidenceRequestStatus.Fulfilled;
        FulfilledAt = now;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        Status = SicknessEvidenceRequestStatus.Cancelled;
        UpdatedAt = now;
    }

    public void MarkOverdue(DateTimeOffset now)
    {
        Status = SicknessEvidenceRequestStatus.Overdue;
        UpdatedAt = now;
    }

    /// <summary>
    /// SICK-01: re-anchors the fit-note deadline once the absence's authoritative end date is
    /// known (i.e. when the record is closed). Only meaningful while the request is still Pending
    /// or Overdue — a fulfilled/cancelled request keeps its history.
    /// </summary>
    public bool Reschedule(DateOnly newDueDate, DateTimeOffset now)
    {
        if (Status != SicknessEvidenceRequestStatus.Pending &&
            Status != SicknessEvidenceRequestStatus.Overdue)
            return false;

        if (DueDate == newDueDate)
            return false;

        DueDate = newDueDate;
        if (Status == SicknessEvidenceRequestStatus.Overdue && newDueDate >= DateOnly.FromDateTime(now.UtcDateTime))
            Status = SicknessEvidenceRequestStatus.Pending;
        UpdatedAt = now;
        return true;
    }
}
