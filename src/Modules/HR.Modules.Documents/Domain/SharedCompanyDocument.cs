using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
namespace HR.Modules.Documents.Domain;

internal sealed class SharedCompanyDocument : IScannableFile, IVersionedAggregate
{
    private SharedCompanyDocument() { }

    // Explicit, persisted optimistic-concurrency token (Ticket 2) — distinct from VersionNumber,
    // which is the uploaded-file revision number. See Employee.Version.
    public int Version { get; private set; } = 1;

    public void IncrementVersion() => Version++;

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

    public Guid? ReviewOwnerEmployeeId { get; private set; }

    public DateOnly? LastReviewedAt { get; private set; }
    public Guid? LastReviewedByEmployeeId { get; private set; }
    public string? LastReviewNotes { get; private set; }

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
        Version                  = 1,
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

    public void Touch(Guid updatedBy, DateTimeOffset now)
    {
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

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

    public void MarkExpired(Guid expiredBy, DateTimeOffset now)
    {
        Status     = SharedCompanyDocumentStatus.Expired;
        ExpiredBy  = expiredBy;
        ExpiredAt  = now;
        UpdatedBy  = expiredBy;
        UpdatedAt  = now;
    }

    public void RevertToDraft(Guid updatedBy, DateTimeOffset now)
    {
        Status    = SharedCompanyDocumentStatus.Draft;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

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
