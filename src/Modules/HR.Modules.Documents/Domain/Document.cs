namespace HR.Modules.Documents.Domain;

internal sealed class Document
{
    private Document() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? EmployeeId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid DocumentTypeId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    public string StorageKey { get; private set; } = string.Empty;
    public DocumentStatus Status { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public Guid UploadedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Document Create(
        Guid id,
        Guid companyId,
        Guid? employeeId,
        string title,
        string? description,
        Guid documentTypeId,
        string fileName,
        long fileSize,
        string contentType,
        string storageKey,
        DateOnly? expiryDate,
        Guid uploadedBy,
        DateTimeOffset now) => new()
    {
        Id             = id,
        CompanyId      = companyId,
        EmployeeId     = employeeId,
        Title          = title.Trim(),
        Description    = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        DocumentTypeId = documentTypeId,
        FileName       = fileName.Trim(),
        FileSize       = fileSize,
        ContentType    = contentType.Trim(),
        StorageKey     = storageKey.Trim(),
        Status         = DocumentStatus.Active,
        ExpiryDate     = expiryDate,
        UploadedBy     = uploadedBy,
        CreatedAt      = now,
        UpdatedAt      = now,
    };

    public void UpdateDetails(
        string title,
        string? description,
        Guid documentTypeId,
        DateOnly? expiryDate,
        DateTimeOffset now)
    {
        Title          = title.Trim();
        Description    = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        DocumentTypeId = documentTypeId;
        ExpiryDate     = expiryDate;
        UpdatedAt      = now;
    }

    public void Archive(DateTimeOffset now)
    {
        Status    = DocumentStatus.Archived;
        UpdatedAt = now;
    }

    public void Expire(DateTimeOffset now)
    {
        Status    = DocumentStatus.Expired;
        UpdatedAt = now;
    }
}
