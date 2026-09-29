using HR.Modules.Recruitment.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace HR.Modules.Recruitment.Tests;

public class SupabaseCandidateDocumentStorageOptionsValidatorTests
{
    private static SupabaseCandidateDocumentStorageOptions ValidOptions() => new()
    {
        SupabaseUrl = "https://example.supabase.co",
        ServiceRoleKey = "service-role-key-value",
        BucketName = "candidate-documents",
        SignedUrlExpirySeconds = 3600,
    };

    private static readonly SupabaseCandidateDocumentStorageOptionsValidator Validator =
        new(new FakeHostEnvironment("Production"));

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
    public void Non_Http_Scheme_SupabaseUrl_Fails()
    {
        var options = ValidOptions();
        options.SupabaseUrl = "ftp://example.supabase.co";

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

    [Fact]
    public void BucketName_With_Whitespace_Fails()
    {
        var options = ValidOptions();
        options.BucketName = "bad bucket name";

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositive_SignedUrlExpirySeconds_Fails(int expiry)
    {
        var options = ValidOptions();
        options.SignedUrlExpirySeconds = expiry;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("SignedUrlExpirySeconds", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void Http_Url_Rejected_In_Staging_Or_Production(string environmentName)
    {
        var validator = new SupabaseCandidateDocumentStorageOptionsValidator(new FakeHostEnvironment(environmentName));
        var options = ValidOptions();
        options.SupabaseUrl = "http://example.supabase.co";

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("https", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void Http_Url_Allowed_In_Development_Or_Test(string environmentName)
    {
        var validator = new SupabaseCandidateDocumentStorageOptionsValidator(new FakeHostEnvironment(environmentName));
        var options = ValidOptions();
        options.SupabaseUrl = "http://example.supabase.co";

        Assert.True(validator.Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Test")]
    public void Https_Url_Passes_In_Every_Environment(string environmentName)
    {
        var validator = new SupabaseCandidateDocumentStorageOptionsValidator(new FakeHostEnvironment(environmentName));
        var options = ValidOptions();

        Assert.True(validator.Validate(null, options).Succeeded);
    }
}

internal sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "HR.Modules.Recruitment.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}
