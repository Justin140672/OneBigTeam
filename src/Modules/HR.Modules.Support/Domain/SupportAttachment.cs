namespace HR.Modules.Support.Domain;

internal sealed class SupportAttachment
{
    private SupportAttachment() { }

    public Guid Id { get; private set; }
    public Guid SupportRequestId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }
    public Guid UploadedByUserId { get; private set; }

    public static SupportAttachment Create(
        Guid id,
        Guid supportRequestId,
        Guid companyId,
        string storageKey,
        string fileName,
        string contentType,
        long sizeBytes,
        Guid uploadedByUserId,
        DateTimeOffset now)
    {
        return new SupportAttachment
        {
            Id = id,
            SupportRequestId = supportRequestId,
            CompanyId = companyId,
            StorageKey = storageKey,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            UploadedByUserId = uploadedByUserId,
            UploadedAt = now
        };
    }
}
