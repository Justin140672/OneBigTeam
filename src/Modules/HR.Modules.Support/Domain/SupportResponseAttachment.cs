namespace HR.Modules.Support.Domain;

internal sealed class SupportResponseAttachment
{
    private SupportResponseAttachment() { }

    public Guid Id { get; private set; }
    public Guid SupportResponseId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; private set; }

    public static SupportResponseAttachment Create(
        Guid id,
        Guid supportResponseId,
        Guid companyId,
        string storageKey,
        string fileName,
        string contentType,
        DateTimeOffset now)
    {
        return new SupportResponseAttachment
        {
            Id = id,
            SupportResponseId = supportResponseId,
            CompanyId = companyId,
            StorageKey = storageKey,
            FileName = fileName,
            ContentType = contentType,
            UploadedAt = now
        };
    }
}
