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

    // DOC-04: recoverable soft-delete/archive state. Normal deletion (DeleteEmployeeDocument)
    // now archives instead of hard-deleting; ArchivedBy/ArchivedAt/ArchiveReason are a permanent
    // record of who archived, when and why — the same "never overwritten by later edits"
    // convention as SharedCompanyDocument.ArchivedBy/ArchivedAt/ArchiveReason. RestoredBy/
    // RestoredAt capture the most recent restore for symmetry with the acceptance criteria
    // ("archive and restore capture actor, timestamp and reason where applicable") — full
    // archive/restore history lives in the audit trail (EmployeeDocumentArchivedAuditEvent /
    // EmployeeDocumentRestoredAuditEvent), these fields just reflect current/most-recent state.
    public bool IsArchived { get; private set; }
    public Guid? ArchivedByUserId { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public string? ArchiveReason { get; private set; }
    public Guid? RestoredByUserId { get; private set; }
    public DateTimeOffset? RestoredAt { get; private set; }

    // DOC-05: version lineage. PreviousVersionId links a replacement upload back to the
    // EmployeeDocument row it supersedes (null for the first version of a lineage).
    // IsLatestVersion is a persisted flag (per the standing "prefer a real column over a
    // computed property" preference) rather than derived by absence of a "next version" FK,
    // because deriving it would require either a reverse-navigation query per row or a
    // NextVersionId column maintained on the OLD row at the moment a new version is created —
    // this is simpler and lets normal list/get queries filter with a single indexed boolean.
    // A new version never mutates or archives the row it replaces; the old row stays exactly as
    // it was (IsArchived unchanged, its own audit trail untouched) with only IsLatestVersion
    // flipped to false, so "previous versions remain immutable and available" holds by
    // construction — nothing about the previous row's content ever changes here.
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

    /// <summary>
    /// DOC-04: recoverable soft-delete. Replaces the previous hard-delete behaviour of
    /// DeleteEmployeeDocumentHandler — no database row is removed and no stored file is deleted;
    /// the record simply becomes excluded from normal list/get/download queries until restored.
    /// Reason is optional (the acceptance criteria says "capture actor, timestamp and reason
    /// where applicable" — a caller may not always supply one), matching DeleteEmployeeDocument's
    /// existing request shape which never required a reason before this ticket.
    /// </summary>
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
