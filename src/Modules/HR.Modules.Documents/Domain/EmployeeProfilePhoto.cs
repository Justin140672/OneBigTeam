namespace HR.Modules.Documents.Domain;

internal sealed class EmployeeProfilePhoto : IScannableFile
{
    private EmployeeProfilePhoto() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    public string StorageKey { get; private set; } = string.Empty;
    public Guid UploadedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public FileScanStatus ScanStatus { get; private set; }
    public DateTimeOffset? ScanCompletedAt { get; private set; }
    public int ScanAttemptCount { get; private set; }
    public string? ScanFailureReason { get; private set; }

    Guid? IScannableFile.EmployeeId => EmployeeId;

    public static EmployeeProfilePhoto Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        string fileName,
        long fileSize,
        string contentType,
        string storageKey,
        Guid uploadedBy,
        DateTimeOffset now) => new()
    {
        Id          = id,
        CompanyId   = companyId,
        EmployeeId  = employeeId,
        FileName    = fileName.Trim(),
        FileSize    = fileSize,
        ContentType = contentType.Trim(),
        StorageKey  = storageKey.Trim(),
        UploadedBy  = uploadedBy,
        CreatedAt   = now,
        UpdatedAt   = now,
        ScanStatus       = FileScanStatus.Pending,
        ScanAttemptCount = 0,
    };

    public void Replace(
        string fileName,
        long fileSize,
        string contentType,
        string storageKey,
        Guid uploadedBy,
        DateTimeOffset now)
    {
        FileName    = fileName.Trim();
        FileSize    = fileSize;
        ContentType = contentType.Trim();
        StorageKey  = storageKey.Trim();
        UploadedBy  = uploadedBy;
        UpdatedAt   = now;
        ScanStatus       = FileScanStatus.Pending;
        ScanCompletedAt  = null;
        ScanFailureReason = null;
        ScanAttemptCount = 0;
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
}
