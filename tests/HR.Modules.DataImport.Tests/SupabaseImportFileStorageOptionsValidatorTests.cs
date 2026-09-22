using HR.Modules.DataImport.Services;
using Xunit;

namespace HR.Modules.DataImport.Tests;

/// <summary>
/// Reliability review issue 3 (P1): startup validation for import file storage must catch every
/// missing/malformed required field, and error messages must name the configuration key without
/// ever printing the secret value itself.
/// </summary>
public class SupabaseImportFileStorageOptionsValidatorTests
{
    private static SupabaseImportFileStorageOptions ValidOptions() => new()
    {
        SupabaseUrl = "https://example.supabase.co",
        ServiceRoleKey = "service-role-key-value",
        BucketName = "import-files",
        SignedUrlExpirySeconds = 3600,
    };

    private static readonly SupabaseImportFileStorageOptionsValidator Validator = new();

    [Fact]
    public void Valid_Options_Pass()
    {
        var result = Validator.Validate(null, ValidOptions());
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Missing_SupabaseUrl_Fails_And_Names_The_Key()
    {
        var options = ValidOptions();
        options.SupabaseUrl = "";

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("SupabaseUrl", StringComparison.Ordinal));
    }

    [Fact]
    public void Malformed_SupabaseUrl_Fails()
    {
        var options = ValidOptions();
        options.SupabaseUrl = "not-a-url";

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Missing_ServiceRoleKey_Fails_Without_Leaking_The_Value()
    {
        var options = ValidOptions();
        options.ServiceRoleKey = "";

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("ServiceRoleKey", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Failures!, f => f.Contains(ValidOptions().ServiceRoleKey, StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_BucketName_Fails()
    {
        var options = ValidOptions();
        options.BucketName = "";

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("BucketName", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositive_SignedUrlExpirySeconds_Fails(int expiry)
    {
        var options = ValidOptions();
        options.SignedUrlExpirySeconds = expiry;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
    }
}
