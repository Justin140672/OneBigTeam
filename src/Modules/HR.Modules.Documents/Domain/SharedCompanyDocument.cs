using HR.Modules.Employees.Contracts;
namespace HR.Modules.Documents.Domain;

/// <summary>
/// A document owned by the company as a whole (e.g. a policy or handbook) rather than by an
/// individual employee — distinct from <see cref="Document"/>, which always belongs to an
/// employee's record. Every query against this entity must filter by CompanyId; there is no
/// EF Core global query filter in this codebase, so tenant isolation is enforced per-handler,
/// the same convention used everywhere else here.
/// </summary>
internal sealed class SharedCompanyDocument : IScannableFile
{
    private SharedCompanyDocument() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid CategoryId { get; private set; }
    public string CurrentFileReference { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    public int VersionNumber { get; private set; }
    public SharedCompanyDocumentStatus Status { get; private set; }
    public DateOnly? EffectiveDate { get; private set; }
    public DateOnly? ReviewDate { get; private set; }
    public SharedCompanyDocumentReviewFrequency ReviewFrequency { get; private set; }
    public int? CustomReviewFrequencyMonths { get; private set; }

    // A company document may not have a review owner assigned — this is a plain Guid reference
    // to an Employee in the Employees module (no navigation property, same as
    // SharedCompanyDocumentAudienceRule.TargetId and Document.EmployeeId elsewhere in this
    // module), resolved to a display name only at the read side via IEmployeeNameReader.
    public Guid? ReviewOwnerEmployeeId { get; private set; }

    // Records the most recently completed review — distinct from ReviewDate, which always holds
    // the *next* scheduled review date (or null once cleared). LastReviewedByEmployeeId is the
    // same "plain Guid, no navigation property" convention as ReviewOwnerEmployeeId above.
    public DateOnly? LastReviewedAt { get; private set; }
    public Guid? LastReviewedByEmployeeId { get; private set; }
    public string? LastReviewNotes { get; private set; }

    // Audience is modelled as a separate set of SharedCompanyDocumentAudienceRule rows (see that
    // type), not fields here — this aggregate has no in-memory audience state of its own, the
    // same way version history lives entirely in SharedCompanyDocumentVersion rows.
    public bool RequiresAcknowledgement { get; private set; }

    // Only meaningful when RequiresAcknowledgement is true. AcknowledgementStatement is
    // deliberately optional with no stored default — "I confirm that I have read and understood
    // this document." is applied as a display-time fallback by callers, not written to the row,
    // so a company can change the default sentence later without rewriting every document.
    public DateOnly? AcknowledgementDueDate { get; private set; }
    public string? AcknowledgementStatement { get; private set; }

    public Guid CreatedBy { get; private set; }
    public Guid UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Distinct from CreatedBy/CreatedAt and UpdatedBy/UpdatedAt: those change on every edit, so
    // without a dedicated pair here "who published this and when" would be silently overwritten
    // by the next metadata or audience change.
    public Guid? PublishedBy { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    // Same rationale as PublishedBy/PublishedAt: a permanent record of who archived this document,
    // when, and why — must not be overwritten by later metadata/audience edits.
    public Guid? ArchivedBy { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public string? ArchiveReason { get; private set; }

    // Same rationale as PublishedBy/PublishedAt and ArchivedBy/ArchivedAt: a permanent record of
    // who marked this document expired and when — must not be overwritten by later metadata/
    // audience edits. Unlike Archive, expiry has no reason field.
    public Guid? ExpiredBy { get; private set; }
    public DateTimeOffset? ExpiredAt { get; private set; }

    public FileScanStatus ScanStatus { get; private set; }
    public DateTimeOffset? ScanCompletedAt { get; private set; }
    public int ScanAttemptCount { get; private set; }
    public string? ScanFailureReason { get; private set; }

    // SharedCompanyDocument has no single owning employee — this entity uses
    // CurrentFileReference (not StorageKey) as its storage pointer, so IScannableFile.StorageKey
    // is implemented via that property below.
    Guid? IScannableFile.EmployeeId => null;
    string IScannableFile.StorageKey => CurrentFileReference;

    public static SharedCompanyDocument Create(
        Guid id,
        Guid companyId,
        string title,
        string? description,
        Guid categoryId,
        string currentFileReference,
        string fileName,
        long fileSize,
        string contentType,
        DateOnly? effectiveDate,
        DateOnly? reviewDate,
        SharedCompanyDocumentReviewFrequency reviewFrequency,
        int? customReviewFrequencyMonths,
        Guid? reviewOwnerEmployeeId,
        bool requiresAcknowledgement,
        DateOnly? acknowledgementDueDate,
        string? acknowledgementStatement,
        Guid createdBy,
        DateTimeOffset now) => new()
    {
        Id                       = id,
        CompanyId                = companyId,
        Title                    = title.Trim(),
        Description              = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        CategoryId               = categoryId,
        CurrentFileReference     = currentFileReference.Trim(),
        FileName                 = fileName.Trim(),
        FileSize                 = fileSize,
        ContentType              = contentType.Trim(),
        VersionNumber            = 1,
        Status                   = SharedCompanyDocumentStatus.Draft,
        EffectiveDate            = effectiveDate,
        ReviewDate               = reviewDate,
        ReviewFrequency             = reviewFrequency,
        CustomReviewFrequencyMonths = reviewFrequency == SharedCompanyDocumentReviewFrequency.Custom ? customReviewFrequencyMonths : null,
        ReviewOwnerEmployeeId    = reviewOwnerEmployeeId,
        RequiresAcknowledgement  = requiresAcknowledgement,
        AcknowledgementDueDate   = requiresAcknowledgement ? acknowledgementDueDate : null,
        AcknowledgementStatement = requiresAcknowledgement && !string.IsNullOrWhiteSpace(acknowledgementStatement)
            ? acknowledgementStatement.Trim()
            : null,
        CreatedBy                = createdBy,
        UpdatedBy                = createdBy,
        CreatedAt                = now,
        UpdatedAt                = now,
        ScanStatus                  = FileScanStatus.Pending,
        ScanAttemptCount             = 0,
    };

    public void UpdateDetails(
        string title,
        string? description,
        Guid categoryId,
        DateOnly? effectiveDate,
        DateOnly? reviewDate,
        SharedCompanyDocumentReviewFrequency reviewFrequency,
        int? customReviewFrequencyMonths,
        Guid? reviewOwnerEmployeeId,
        Guid updatedBy,
        DateTimeOffset now)
    {
        Title                   = title.Trim();
        Description             = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        CategoryId              = categoryId;
        EffectiveDate           = effectiveDate;
        ReviewDate              = reviewDate;
        ReviewFrequency             = reviewFrequency;
        CustomReviewFrequencyMonths = reviewFrequency == SharedCompanyDocumentReviewFrequency.Custom ? customReviewFrequencyMonths : null;
        ReviewOwnerEmployeeId   = reviewOwnerEmployeeId;
        UpdatedBy               = updatedBy;
        UpdatedAt               = now;
    }

    /// <summary>
    /// Replaces all three acknowledgement settings together — due date and statement are only
    /// ever meaningful alongside RequiresAcknowledgement, so this deliberately clears both when
    /// acknowledgement is turned off rather than leaving stale values behind.
    /// </summary>
    public void SetAcknowledgementSettings(
        bool requiresAcknowledgement,
        DateOnly? acknowledgementDueDate,
        string? acknowledgementStatement,
        Guid updatedBy,
        DateTimeOffset now)
    {
        RequiresAcknowledgement  = requiresAcknowledgement;
        AcknowledgementDueDate   = requiresAcknowledgement ? acknowledgementDueDate : null;
        AcknowledgementStatement = requiresAcknowledgement && !string.IsNullOrWhiteSpace(acknowledgementStatement)
            ? acknowledgementStatement.Trim()
            : null;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    /// <summary>
    /// Bumps UpdatedBy/UpdatedAt without changing any other field — used when something owned
    /// outside this aggregate changes (e.g. its audience rule rows) but should still show up as
    /// a "last updated" change on the document itself.
    /// </summary>
    public void Touch(Guid updatedBy, DateTimeOffset now)
    {
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    /// <summary>Uploads a new version of the file, replacing the current one and incrementing VersionNumber.</summary>
    public void ReplaceFile(string newFileReference, string fileName, long fileSize, string contentType, Guid updatedBy, DateTimeOffset now)
    {
        CurrentFileReference = newFileReference.Trim();
        FileName             = fileName.Trim();
        FileSize             = fileSize;
        ContentType          = contentType.Trim();
        VersionNumber++;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
        ScanStatus        = FileScanStatus.Pending;
        ScanCompletedAt   = null;
        ScanFailureReason = null;
        ScanAttemptCount  = 0;
    }

    public void MarkScanning(DateTimeOffset now)
    {
        ScanStatus = FileScanStatus.Scanning;
        ScanAttemptCount++;
        UpdatedAt = now;
    }

    public void MarkScanClean(DateTimeOffset now)
    {
        ScanStatus = FileScanStatus.Clean;
        ScanCompletedAt = now;
        ScanFailureReason = null;
        UpdatedAt = now;
    }

    public void MarkScanInfected(string threatName, DateTimeOffset now)
    {
        ScanStatus = FileScanStatus.Infected;
        ScanCompletedAt = now;
        ScanFailureReason = threatName;
        UpdatedAt = now;
    }

    public void MarkScanFailed(string reason, DateTimeOffset now)
    {
        ScanStatus = FileScanStatus.Failed;
        ScanCompletedAt = now;
        ScanFailureReason = reason;
        UpdatedAt = now;
    }

    public void Publish(Guid publishedBy, DateTimeOffset now)
    {
        Status      = SharedCompanyDocumentStatus.Published;
        PublishedBy = publishedBy;
        PublishedAt = now;
        UpdatedBy   = publishedBy;
        UpdatedAt   = now;
    }

    public void Archive(Guid archivedBy, string reason, DateTimeOffset now)
    {
        Status        = SharedCompanyDocumentStatus.Archived;
        ArchivedBy    = archivedBy;
        ArchivedAt    = now;
        ArchiveReason = reason.Trim();
        UpdatedBy     = archivedBy;
        UpdatedAt     = now;
    }

    /// <summary>
    /// Marks the document Expired instead of being renewed — a terminal state distinct from
    /// Archive: no reason is captured (the ticket does not ask for one, unlike Archive which
    /// requires a reason). Expired documents are excluded from employee-facing reads via the
    /// same strict equality-against-Published filter those handlers already use, so no changes
    /// are needed there.
    /// </summary>
    public void MarkExpired(Guid expiredBy, DateTimeOffset now)
    {
        Status     = SharedCompanyDocumentStatus.Expired;
        ExpiredBy  = expiredBy;
        ExpiredAt  = now;
        UpdatedBy  = expiredBy;
        UpdatedAt  = now;
    }

    /// <summary>Moves a Published document back to Draft (e.g. to correct a mistake before republishing).</summary>
    public void RevertToDraft(Guid updatedBy, DateTimeOffset now)
    {
        Status    = SharedCompanyDocumentStatus.Draft;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records a completed review and moves ReviewDate forward to the next scheduled review date.
    /// nextReviewDate is computed by the caller (based on ReviewFrequency/CustomReviewFrequencyMonths)
    /// — this method has no knowledge of review cadence, the same way UpdateDetails receives
    /// reviewDate/reviewFrequency as given values rather than computing anything itself.
    /// </summary>
    public void CompleteReview(Guid reviewedBy, string reviewNotes, DateOnly reviewDate, DateOnly? nextReviewDate, DateTimeOffset now)
    {
        LastReviewedAt           = reviewDate;
        LastReviewedByEmployeeId = reviewedBy;
        LastReviewNotes          = string.IsNullOrWhiteSpace(reviewNotes) ? null : reviewNotes.Trim();
        ReviewDate               = nextReviewDate;
        UpdatedBy                = reviewedBy;
        UpdatedAt                = now;
    }
}
