namespace HR.Modules.Recruitment.Domain;

internal sealed class CandidateDocument
{
    private CandidateDocument() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CandidateId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    public string StorageKey { get; private set; } = string.Empty;
    public Guid UploadedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static CandidateDocument Create(
        Guid id,
        Guid companyId,
        Guid candidateId,
        string title,
        string fileName,
        long fileSize,
        string contentType,
        string storageKey,
        Guid uploadedBy,
        DateTimeOffset now) => new()
    {
        Id          = id,
        CompanyId   = companyId,
        CandidateId = candidateId,
        Title       = title.Trim(),
        FileName    = fileName.Trim(),
        FileSize    = fileSize,
        ContentType = contentType.Trim(),
        StorageKey  = storageKey.Trim(),
        UploadedBy  = uploadedBy,
        CreatedAt   = now,
    };
}
