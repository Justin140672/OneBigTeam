namespace HR.Infrastructure.Storage;

internal sealed class SupabaseSupportAttachmentStorageOptions
{
    public string SupabaseUrl { get; set; } = string.Empty;
    public string ServiceRoleKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = "support-attachments";
    public int SignedUrlExpirySeconds { get; set; } = 3600;
}
