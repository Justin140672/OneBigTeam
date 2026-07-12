namespace HR.Modules.Documents.Domain;

internal sealed class EmployeeProfilePhoto
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
    }
}
