namespace HR.Infrastructure.Storage;

internal sealed class SupabaseProfilePhotoStorageOptions
{
    public string SupabaseUrl { get; set; } = string.Empty;
    public string ServiceRoleKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = "profile-photos";
    public int SignedUrlExpirySeconds { get; set; } = 3600;
}
