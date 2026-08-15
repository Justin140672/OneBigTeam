using HR.Modules.Companies.Features.UpdatePlatformSettings;

namespace HR.Modules.Companies.Tests;

public class UpdatePlatformSettingsValidatorTests
{
    private static UpdatePlatformSettingsRequest ValidRequest() => new(
        TrialLengthDays: 14,
        DefaultMonthlyPriceGbp: 9.99m,
        SupportEmail: "support@example.com",
        MaintenanceModeEnabled: false,
        MaintenanceModeMessage: null,
        FeatureFlags: new Dictionary<string, bool>());

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = new UpdatePlatformSettingsValidator().Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_TrialLengthDays_Is_Zero()
    {
        var result = new UpdatePlatformSettingsValidator().Validate(ValidRequest() with { TrialLengthDays = 0 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePlatformSettingsRequest.TrialLengthDays));
    }

    [Fact]
    public void Validate_Fails_When_TrialLengthDays_Is_Negative()
    {
        var result = new UpdatePlatformSettingsValidator().Validate(ValidRequest() with { TrialLengthDays = -5 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePlatformSettingsRequest.TrialLengthDays));
    }

    [Fact]
    public void Validate_Fails_When_DefaultMonthlyPriceGbp_Is_Negative()
    {
        var result = new UpdatePlatformSettingsValidator().Validate(ValidRequest() with { DefaultMonthlyPriceGbp = -0.01m });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePlatformSettingsRequest.DefaultMonthlyPriceGbp));
    }

    [Fact]
    public void Validate_Passes_When_DefaultMonthlyPriceGbp_Is_Zero()
    {
        var result = new UpdatePlatformSettingsValidator().Validate(ValidRequest() with { DefaultMonthlyPriceGbp = 0m });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_SupportEmail_Is_Missing()
    {
        var result = new UpdatePlatformSettingsValidator().Validate(ValidRequest() with { SupportEmail = string.Empty });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePlatformSettingsRequest.SupportEmail));
    }

    [Fact]
    public void Validate_Fails_When_SupportEmail_Has_Invalid_Format()
    {
        var result = new UpdatePlatformSettingsValidator().Validate(ValidRequest() with { SupportEmail = "not-an-email" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePlatformSettingsRequest.SupportEmail));
    }

    [Fact]
    public void Validate_Fails_When_MaintenanceModeMessage_Exceeds_2000_Characters()
    {
        var result = new UpdatePlatformSettingsValidator().Validate(
            ValidRequest() with { MaintenanceModeMessage = new string('A', 2001) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdatePlatformSettingsRequest.MaintenanceModeMessage));
    }

    [Fact]
    public void Validate_Passes_When_MaintenanceModeMessage_Is_Null()
    {
        var result = new UpdatePlatformSettingsValidator().Validate(ValidRequest() with { MaintenanceModeMessage = null });

        Assert.True(result.IsValid);
    }
}
