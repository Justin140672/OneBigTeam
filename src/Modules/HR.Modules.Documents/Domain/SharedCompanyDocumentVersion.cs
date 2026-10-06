namespace HR.Modules.Documents.Domain;

internal sealed class SharedCompanyDocumentVersion : IScannableFile
{
    private SharedCompanyDocumentVersion() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SharedCompanyDocumentId { get; private set; }
    public int VersionNumber { get; private set; }
    public string FileReference { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public string? VersionNote { get; private set; }
    public bool RequiresAcknowledgement { get; private set; }

    public DateOnly? EffectiveDate { get; private set; }

    // Independent, point-in-time copy of the acknowledgement statement wording in effect when
    // this version was created — nullable because a version that never required acknowledgement
    // has none. Never mutated after creation; a later edit to
    // SharedCompanyDocument.AcknowledgementStatement (the mutable "current" value) must not
    // change what earlier versions recorded here.
    public string? AcknowledgementStatement { get; private set; }

    public FileScanStatus ScanStatus { get; private set; }
    public DateTimeOffset? ScanCompletedAt { get; private set; }
    public int ScanAttemptCount { get; private set; }
    public string? ScanFailureReason { get; private set; }

    Guid? IScannableFile.EmployeeId => null;
    string IScannableFile.StorageKey => FileReference;

    public static SharedCompanyDocumentVersion Create(
        Guid id,
        Guid companyId,
        Guid sharedCompanyDocumentId,
        int versionNumber,
        string fileReference,
        string fileName,
        long fileSize,
        string contentType,
        Guid createdBy,
        DateTimeOffset now,
        string? versionNote,
        bool requiresAcknowledgement,
        DateOnly? effectiveDate,
        string? acknowledgementStatement = null) => new()
    {
        Id                      = id,
        CompanyId               = companyId,
        SharedCompanyDocumentId = sharedCompanyDocumentId,
        VersionNumber           = versionNumber,
        FileReference           = fileReference.Trim(),
        FileName                = fileName.Trim(),
        FileSize                = fileSize,
        ContentType             = contentType.Trim(),
        CreatedBy               = createdBy,
        CreatedAt               = now,
        VersionNote             = string.IsNullOrWhiteSpace(versionNote) ? null : versionNote.Trim(),
        RequiresAcknowledgement = requiresAcknowledgement,
        EffectiveDate           = effectiveDate,
        AcknowledgementStatement = string.IsNullOrWhiteSpace(acknowledgementStatement) ? null : acknowledgementStatement.Trim(),
        ScanStatus               = FileScanStatus.Pending,
        ScanAttemptCount         = 0,
    };

    public void MarkScanning(DateTimeOffset now)
    {
        ScanStatus = FileScanStatus.Scanning;
        ScanAttemptCount++;
    }

    public void MarkScanClean(DateTimeOffset now)
    {
        if (ScanStatus is FileScanStatus.Infected or FileScanStatus.Failed)
            return;

        ScanStatus = FileScanStatus.Clean;
        ScanCompletedAt = now;
        ScanFailureReason = null;
    }

    public void MarkScanInfected(string threatName, DateTimeOffset now)
    {
        ScanStatus = FileScanStatus.Infected;
        ScanCompletedAt = now;
        ScanFailureReason = threatName;
    }

    public void MarkScanFailed(string reason, DateTimeOffset now)
    {
        ScanStatus = FileScanStatus.Failed;
        ScanCompletedAt = now;
        ScanFailureReason = reason;
    }
}
