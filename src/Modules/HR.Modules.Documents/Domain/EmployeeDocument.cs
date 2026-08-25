namespace HR.Modules.Documents.Domain;

internal sealed class EmployeeDocument
{
    private EmployeeDocument() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid AddedBy { get; private set; }
    public DateOnly? IssueDate { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public DateTimeOffset? ExpiringSoonNotifiedAt { get; private set; }
    public DateTimeOffset? ExpiredNotifiedAt { get; private set; }

    // DOC-03: three independent, one-shot reminder stages fired as the expiry date approaches.
    // Each is set once (when the corresponding day-threshold is first crossed by the daily job)
    // and never re-set unless UpdateExpiryDate restarts the schedule against a new expiry date.
    public DateTimeOffset? ExpiryReminder90SentAt { get; private set; }
    public DateTimeOffset? ExpiryReminder30SentAt { get; private set; }
    public DateTimeOffset? ExpiryReminder7SentAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static EmployeeDocument Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid documentId,
        Guid addedBy,
        DateTimeOffset now,
        DateOnly? issueDate  = null,
        DateOnly? expiryDate = null) => new()
    {
        Id         = id,
        CompanyId  = companyId,
        EmployeeId = employeeId,
        DocumentId = documentId,
        AddedBy    = addedBy,
        IssueDate  = issueDate,
        ExpiryDate = expiryDate,
        CreatedAt  = now,
        UpdatedAt  = now,
    };

    public DocumentExpiryStatus GetExpiryStatus(DateOnly today) => ExpiryDate switch
    {
        null                                      => DocumentExpiryStatus.Valid,
        var d when d < today                      => DocumentExpiryStatus.Expired,
        var d when d <= today.AddDays(30)         => DocumentExpiryStatus.ExpiringSoon,
        _                                         => DocumentExpiryStatus.Valid,
    };

    public void Acknowledge(DateTimeOffset now)
    {
        AcknowledgedAt = now;
        UpdatedAt      = now;
    }

    public void MarkExpiringSoonNotified(DateTimeOffset now)
    {
        ExpiringSoonNotifiedAt = now;
        UpdatedAt              = now;
    }

    public void MarkExpiredNotified(DateTimeOffset now)
    {
        ExpiredNotifiedAt = now;
        UpdatedAt         = now;
    }

    /// <summary>
    /// DOC-03: marks the given upcoming-expiry reminder stage as sent. Each stage is independent
    /// and idempotent — calling this again for a stage that has already fired is a safe no-op at
    /// the call site (callers should check the corresponding *SentAt property first), and the
    /// value is never overwritten once set except via <see cref="UpdateExpiryDate"/>.
    /// </summary>
    public void MarkExpiryReminderSent(ExpiryReminderStage stage, DateTimeOffset now)
    {
        switch (stage)
        {
            case ExpiryReminderStage.NinetyDays:
                ExpiryReminder90SentAt = now;
                break;
            case ExpiryReminderStage.ThirtyDays:
                ExpiryReminder30SentAt = now;
                break;
            case ExpiryReminderStage.SevenDays:
                ExpiryReminder7SentAt = now;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
        }

        UpdatedAt = now;
    }

    /// <summary>
    /// DOC-03: updates the tracked expiry date and resets all reminder state (the three upcoming
    /// -expiry stages plus the legacy expiring-soon/expired flags) so the notification schedule
    /// restarts cleanly against the new date. Callers must always go through this method rather
    /// than setting ExpiryDate directly, so the reset can never be forgotten at a future call
    /// site (e.g. a document re-issue/edit handler).
    /// </summary>
    public void UpdateExpiryDate(DateOnly? newExpiryDate, DateTimeOffset now)
    {
        ExpiryDate = newExpiryDate;

        ExpiryReminder90SentAt = null;
        ExpiryReminder30SentAt = null;
        ExpiryReminder7SentAt  = null;
        ExpiringSoonNotifiedAt = null;
        ExpiredNotifiedAt      = null;

        UpdatedAt = now;
    }
}

internal enum ExpiryReminderStage
{
    NinetyDays,
    ThirtyDays,
    SevenDays,
}
