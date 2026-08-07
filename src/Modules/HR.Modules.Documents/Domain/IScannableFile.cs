namespace HR.Modules.Documents.Domain;

/// <summary>
/// Common shape shared by every entity that owns an uploaded file subject to virus scanning
/// (Document, EmployeeProfilePhoto, PendingProfilePhoto, SharedCompanyDocument,
/// SharedCompanyDocumentVersion). Lets ScanUploadedFileJob operate on any of the five without
/// scanner- or entity-specific branching beyond picking which DbSet to query.
/// </summary>
internal interface IScannableFile
{
    string StorageKey { get; }
    string FileName { get; }
    Guid? EmployeeId { get; }
    FileScanStatus ScanStatus { get; }

    void MarkScanning(DateTimeOffset now);
    void MarkScanClean(DateTimeOffset now);
    void MarkScanInfected(string threatName, DateTimeOffset now);
    void MarkScanFailed(string reason, DateTimeOffset now);
}
