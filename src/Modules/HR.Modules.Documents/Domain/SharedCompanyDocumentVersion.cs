namespace HR.Modules.Documents.Domain;

/// <summary>
/// A point-in-time record of one uploaded file for a <see cref="SharedCompanyDocument"/>. A row
/// is written every time the document is created or its file is replaced, so "version history"
/// is always a complete list (including the current version), not just past ones.
/// </summary>
internal sealed class SharedCompanyDocumentVersion
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
        DateTimeOffset now) => new()
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
    };
}
