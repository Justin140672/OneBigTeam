using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests;

public class CompanySettingsWorkEmailSettingsTests
{
    [Fact]
    public void CreateDefault_Sets_Default_WorkEmailSettings()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.True(settings.WorkEmailSuggestionsEnabled);
        Assert.Null(settings.WorkEmailPrimaryDomain);
        Assert.Equal(WorkEmailNamingConvention.FirstNameDotLastName, settings.WorkEmailNamingConvention);
    }

    [Fact]
    public void UpdateWorkEmailSettings_Sets_New_Values()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        settings.UpdateWorkEmailSettings(
            true, "example.com", WorkEmailNamingConvention.FirstInitialDotLastName, DateTimeOffset.UtcNow);

        Assert.True(settings.WorkEmailSuggestionsEnabled);
        Assert.Equal("example.com", settings.WorkEmailPrimaryDomain);
        Assert.Equal(WorkEmailNamingConvention.FirstInitialDotLastName, settings.WorkEmailNamingConvention);
    }

    [Fact]
    public void UpdateWorkEmailSettings_Normalises_Primary_Domain()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        settings.UpdateWorkEmailSettings(
            true, "  @Example.COM ", WorkEmailNamingConvention.FirstName, DateTimeOffset.UtcNow);

        Assert.Equal("example.com", settings.WorkEmailPrimaryDomain);
    }

    [Fact]
    public void UpdateWorkEmailSettings_Allows_Suggestions_Disabled_And_Keeps_Domain_And_Convention()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        settings.UpdateWorkEmailSettings(false, "example.com", WorkEmailNamingConvention.FirstName, DateTimeOffset.UtcNow);

        Assert.False(settings.WorkEmailSuggestionsEnabled);
        Assert.Equal("example.com", settings.WorkEmailPrimaryDomain);
        Assert.Equal(WorkEmailNamingConvention.FirstName, settings.WorkEmailNamingConvention);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@")]
    [InlineData("not a domain")]
    [InlineData("localhost")]
    public void UpdateWorkEmailSettings_Rejects_Missing_Or_Invalid_Primary_Domain_And_Leaves_State_Unchanged(string domain)
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);
        settings.UpdateWorkEmailSettings(true, "example.com", WorkEmailNamingConvention.FirstName, DateTimeOffset.UtcNow);
        var version = settings.Version;

        Assert.Throws<ArgumentException>(() =>
            settings.UpdateWorkEmailSettings(true, domain, WorkEmailNamingConvention.FirstNameLastName, DateTimeOffset.UtcNow));

        Assert.Equal("example.com", settings.WorkEmailPrimaryDomain);
        Assert.Equal(WorkEmailNamingConvention.FirstName, settings.WorkEmailNamingConvention);
        Assert.Equal(version, settings.Version);
    }

    [Fact]
    public void UpdateWorkEmailSettings_Rejects_Undefined_Naming_Convention()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            settings.UpdateWorkEmailSettings(true, "example.com", (WorkEmailNamingConvention)999, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void UpdateWorkEmailSettings_Updates_UpdatedAt_And_Bumps_Version()
    {
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), createdAt);
        var versionBefore = settings.Version;
        var updatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        settings.UpdateWorkEmailSettings(true, "example.com", WorkEmailNamingConvention.FirstName, updatedAt);

        Assert.Equal(updatedAt, settings.UpdatedAt);
        Assert.Equal(createdAt, settings.CreatedAt);
        Assert.Equal(versionBefore + 1, settings.Version);
    }
}
