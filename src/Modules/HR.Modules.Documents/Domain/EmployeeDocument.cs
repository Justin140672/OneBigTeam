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

    public DateTimeOffset? ExpiryReminder90SentAt { get; private set; }
    public DateTimeOffset? ExpiryReminder30SentAt { get; private set; }
    public DateTimeOffset? ExpiryReminder7SentAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsArchived { get; private set; }
    public Guid? ArchivedByUserId { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public string? ArchiveReason { get; private set; }
    public Guid? RestoredByUserId { get; private set; }
    public DateTimeOffset? RestoredAt { get; private set; }

    public Guid? PreviousVersionId { get; private set; }
    public bool IsLatestVersion { get; private set; }

    public static EmployeeDocument Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid documentId,
        Guid addedBy,
        DateTimeOffset now,
        DateOnly? issueDate  = null,
        DateOnly? expiryDate = null,
        Guid? previousVersionId = null) => new()
    {
        Id                = id,
        CompanyId         = companyId,
        EmployeeId        = employeeId,
        DocumentId        = documentId,
        AddedBy           = addedBy,
        IssueDate         = issueDate,
        ExpiryDate        = expiryDate,
        CreatedAt         = now,
        UpdatedAt         = now,
        PreviousVersionId = previousVersionId,
        IsLatestVersion   = true,
    };

    /// <summary>
    /// DOC-05: marks this row as no longer the latest version, because a replacement has just
    /// been uploaded (see UploadEmployeeDocumentVersionHandler). Deliberately does nothing else —
    /// no archive flag, no expiry/reminder state change, no metadata edit — so this row's own
    /// audit history (uploads, downloads, prior archive/restore events) is preserved exactly as
    /// it was; it simply stops being returned by normal "current documents" views and only
    /// remains reachable through GetEmployeeDocumentVersionHistory.
    /// </summary>
    public void SupersedeAsPreviousVersion(DateTimeOffset now)
    {
        IsLatestVersion = false;
        UpdatedAt       = now;
    }

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

    public void Archive(Guid archivedBy, string? reason, DateTimeOffset now)
    {
        IsArchived       = true;
        ArchivedByUserId = archivedBy;
        ArchivedAt       = now;
        ArchiveReason    = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedAt        = now;
    }

    /// <summary>
    /// DOC-04: reverses <see cref="Archive"/>, making the document visible again in normal
    /// list/get/download queries. Deliberately clears the archive fields rather than keeping
    /// them for "was previously archived" history on the entity itself — that history is
    /// preserved in the audit trail (EmployeeDocumentArchivedAuditEvent /
    /// EmployeeDocumentRestoredAuditEvent), not on the row.
    /// </summary>
    public void Restore(Guid restoredBy, DateTimeOffset now)
    {
        IsArchived       = false;
        ArchivedByUserId = null;
        ArchivedAt       = null;
        ArchiveReason    = null;
        RestoredByUserId = restoredBy;
        RestoredAt       = now;
        UpdatedAt        = now;
    }
}

internal enum ExpiryReminderStage
{
    NinetyDays,
    ThirtyDays,
    SevenDays,
}
