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

    /// <summary>
    /// OBT-REM-10: independent, durable progress marker for the overdue in-app notification —
    /// tracked separately from <see cref="OverdueEventPublishedAt"/> so a partial failure (e.g.
    /// notification written but event publication fails) can be repaired on retry without
    /// re-notifying or silently losing the event. Set once, on first successful write.
    /// </summary>
    public DateTimeOffset? OverdueNotifiedAt { get; private set; }

    /// <summary>
    /// OBT-REM-10: independent, durable progress marker for overdue integration-event
    /// publication. See <see cref="OverdueNotifiedAt"/>.
    /// </summary>
    public DateTimeOffset? OverdueEventPublishedAt { get; private set; }

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

    /// <summary>Idempotent: safe to call even if already marked (no-op on second call).</summary>
    public void MarkOverdueNotified(DateTimeOffset now)
    {
        if (OverdueNotifiedAt is not null) return;
        OverdueNotifiedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Idempotent: safe to call even if already marked (no-op on second call).</summary>
    public void MarkOverdueEventPublished(DateTimeOffset now)
    {
        if (OverdueEventPublishedAt is not null) return;
        OverdueEventPublishedAt = now;
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
        {
            Status = SicknessEvidenceRequestStatus.Pending;
            // Re-anchoring back into the future means a subsequent overdue transition is a new
            // occurrence — reset the progress markers so it is notified/published again.
            OverdueNotifiedAt = null;
            OverdueEventPublishedAt = null;
        }
        UpdatedAt = now;
        return true;
    }
}
