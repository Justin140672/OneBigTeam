using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests;

public class PlatformSettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateDefault_Produces_Expected_Defaults()
    {
        var settings = PlatformSettings.CreateDefault(Now);

        Assert.Equal(PlatformSettings.SingletonId, settings.Id);
        Assert.Equal(14, settings.TrialLengthDays);
        Assert.Equal(20.00m, settings.DefaultMonthlyPriceGbp);
        Assert.Equal("support@hrplatform.com", settings.SupportEmail);
        Assert.False(settings.MaintenanceModeEnabled);
        Assert.Null(settings.MaintenanceModeMessage);
        Assert.Equal("{}", settings.FeatureFlagsJson);
        Assert.Equal(Now, settings.UpdatedAt);
        Assert.Null(settings.UpdatedByUserId);
    }

    [Fact]
    public void Update_Succeeds_With_Valid_Input_And_Mutates_All_Fields()
    {
        var settings = PlatformSettings.CreateDefault(Now);
        var updatedByUserId = Guid.NewGuid();
        var updateAt = Now.AddDays(1);
        var featureFlagsJson = "{\"beta\":true}";

        var result = settings.Update(
            trialLengthDays: 30,
            defaultMonthlyPriceGbp: 49.99m,
            supportEmail: "help@example.com",
            maintenanceModeEnabled: true,
            maintenanceModeMessage: "Down for maintenance",
            featureFlagsJson: featureFlagsJson,
            updatedByUserId: updatedByUserId,
            now: updateAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(30, settings.TrialLengthDays);
        Assert.Equal(49.99m, settings.DefaultMonthlyPriceGbp);
        Assert.Equal("help@example.com", settings.SupportEmail);
        Assert.True(settings.MaintenanceModeEnabled);
        Assert.Equal("Down for maintenance", settings.MaintenanceModeMessage);
        Assert.Equal(featureFlagsJson, settings.FeatureFlagsJson);
        Assert.Equal(updatedByUserId, settings.UpdatedByUserId);
        Assert.Equal(updateAt, settings.UpdatedAt);
    }

    [Fact]
    public void Update_Fails_When_TrialLengthDays_Is_Zero()
    {
        var settings = PlatformSettings.CreateDefault(Now);

        var result = settings.Update(0, 10, "help@example.com", false, null, "{}", null, Now.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Update_Fails_When_TrialLengthDays_Is_Negative()
    {
        var settings = PlatformSettings.CreateDefault(Now);

        var result = settings.Update(-1, 10, "help@example.com", false, null, "{}", null, Now.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Update_Fails_When_DefaultMonthlyPriceGbp_Is_Negative()
    {
        var settings = PlatformSettings.CreateDefault(Now);

        var result = settings.Update(14, -0.01m, "help@example.com", false, null, "{}", null, Now.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Update_Fails_When_SupportEmail_Is_Null()
    {
        var settings = PlatformSettings.CreateDefault(Now);

        var result = settings.Update(14, 10, null!, false, null, "{}", null, Now.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Update_Fails_When_SupportEmail_Is_Whitespace()
    {
        var settings = PlatformSettings.CreateDefault(Now);

        var result = settings.Update(14, 10, "   ", false, null, "{}", null, Now.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Failed_Update_Does_Not_Mutate_State()
    {
        var settings = PlatformSettings.CreateDefault(Now);

        var result = settings.Update(0, 10, "help@example.com", true, "msg", "{\"x\":true}", Guid.NewGuid(), Now.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Equal(14, settings.TrialLengthDays);
        Assert.Equal(20.00m, settings.DefaultMonthlyPriceGbp);
        Assert.Equal("support@hrplatform.com", settings.SupportEmail);
        Assert.False(settings.MaintenanceModeEnabled);
        Assert.Null(settings.MaintenanceModeMessage);
        Assert.Equal("{}", settings.FeatureFlagsJson);
        Assert.Null(settings.UpdatedByUserId);
        Assert.Equal(Now, settings.UpdatedAt);
    }
}
