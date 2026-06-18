namespace HR.Modules.Documents.Services;

internal sealed class SupabaseStorageOptions
{
    public string SupabaseUrl { get; set; } = string.Empty;
    public string ServiceRoleKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public int SignedUrlExpirySeconds { get; set; } = 3600;
}
