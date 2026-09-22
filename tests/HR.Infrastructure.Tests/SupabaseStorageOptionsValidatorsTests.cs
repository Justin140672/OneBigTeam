using HR.Infrastructure.Storage;
using Xunit;

namespace HR.Infrastructure.Tests;

/// <summary>
/// Reliability review issue 3 (P1): startup validation for profile photo / support attachment /
/// organisation export storage must catch every missing/malformed required field, not just a blank
/// SupabaseUrl, and must never print the secret value itself in an error message.
/// </summary>
public class SupabaseStorageOptionsValidatorsTests
{
    [Fact]
    public void ProfilePhoto_Valid_Options_Pass()
    {
        var validator = new SupabaseProfilePhotoStorageOptionsValidator();
        var options = new SupabaseProfilePhotoStorageOptions
        {
            SupabaseUrl = "https://example.supabase.co",
            ServiceRoleKey = "key",
            BucketName = "profile-photos",
            SignedUrlExpirySeconds = 3600,
        };

        Assert.True(validator.Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("", "key", "bucket", 3600)]
    [InlineData("not-a-url", "key", "bucket", 3600)]
    [InlineData("https://example.supabase.co", "", "bucket", 3600)]
    [InlineData("https://example.supabase.co", "key", "", 3600)]
    [InlineData("https://example.supabase.co", "key", "bucket", 0)]
    [InlineData("https://example.supabase.co", "key", "bucket", -1)]
    public void ProfilePhoto_Invalid_Options_Fail(string url, string key, string bucket, int expiry)
    {
        var validator = new SupabaseProfilePhotoStorageOptionsValidator();
        var options = new SupabaseProfilePhotoStorageOptions
        {
            SupabaseUrl = url,
            ServiceRoleKey = key,
            BucketName = bucket,
            SignedUrlExpirySeconds = expiry,
        };

        var result = validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.DoesNotContain(result.Failures!, f => f.Contains("key", StringComparison.Ordinal) && key.Length > 0);
    }

    [Fact]
    public void SupportAttachment_Valid_Options_Pass()
    {
        var validator = new SupabaseSupportAttachmentStorageOptionsValidator();
        var options = new SupabaseSupportAttachmentStorageOptions
        {
            SupabaseUrl = "https://example.supabase.co",
            ServiceRoleKey = "key",
            BucketName = "support-attachments",
            SignedUrlExpirySeconds = 3600,
        };

        Assert.True(validator.Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("", "key", "bucket")]
    [InlineData("https://example.supabase.co", "", "bucket")]
    [InlineData("https://example.supabase.co", "key", "")]
    public void SupportAttachment_Invalid_Options_Fail(string url, string key, string bucket)
    {
        var validator = new SupabaseSupportAttachmentStorageOptionsValidator();
        var options = new SupabaseSupportAttachmentStorageOptions
        {
            SupabaseUrl = url,
            ServiceRoleKey = key,
            BucketName = bucket,
            SignedUrlExpirySeconds = 3600,
        };

        Assert.True(validator.Validate(null, options).Failed);
    }

    [Fact]
    public void OrganisationExport_Valid_Options_Pass()
    {
        var validator = new SupabaseOrganisationDataExportStorageOptionsValidator();
        var options = new SupabaseOrganisationDataExportStorageOptions
        {
            SupabaseUrl = "https://example.supabase.co",
            ServiceRoleKey = "key",
            BucketName = "organisation-exports",
        };

        Assert.True(validator.Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("", "key", "bucket")]
    [InlineData("https://example.supabase.co", "", "bucket")]
    [InlineData("https://example.supabase.co", "key", "")]
    public void OrganisationExport_Invalid_Options_Fail(string url, string key, string bucket)
    {
        var validator = new SupabaseOrganisationDataExportStorageOptionsValidator();
        var options = new SupabaseOrganisationDataExportStorageOptions
        {
            SupabaseUrl = url,
            ServiceRoleKey = key,
            BucketName = bucket,
        };

        Assert.True(validator.Validate(null, options).Failed);
    }
}
