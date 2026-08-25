using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests.Domain;

public class CompanySettingsValidationTests
{
    [Fact]
    public void TryResolveTimeZone_Resolves_Known_Id()
    {
        var resolved = CompanySettingsValidation.TryResolveTimeZone("UTC", out var canonicalId);

        Assert.True(resolved);
        Assert.NotEmpty(canonicalId);
    }

    [Fact]
    public void TryResolveTimeZone_Returns_False_For_Unrecognised_Id()
    {
        var resolved = CompanySettingsValidation.TryResolveTimeZone("Not/A_Real_Zone", out var canonicalId);

        Assert.False(resolved);
        Assert.Equal(string.Empty, canonicalId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolveTimeZone_Returns_False_For_Null_Empty_Or_Whitespace(string? timeZone)
    {
        var resolved = CompanySettingsValidation.TryResolveTimeZone(timeZone, out var canonicalId);

        Assert.False(resolved);
        Assert.Equal(string.Empty, canonicalId);
    }

    [Theory]
    [InlineData("en-GB")]
    [InlineData("ga-IE")]
    public void IsSupportedLocale_Returns_True_For_Listed_Locale(string locale)
    {
        Assert.True(CompanySettingsValidation.IsSupportedLocale(locale));
    }

    [Fact]
    public void IsSupportedLocale_Returns_False_For_WellFormed_But_Unlisted_Locale()
    {
        Assert.False(CompanySettingsValidation.IsSupportedLocale("xx-XX"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsSupportedLocale_Returns_False_For_Null_Empty_Or_Whitespace(string? locale)
    {
        Assert.False(CompanySettingsValidation.IsSupportedLocale(locale));
    }
}
