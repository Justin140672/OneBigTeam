using HR.Modules.Companies.Features.UpdateNotificationSettings;

namespace HR.Modules.Companies.Tests;

public class UpdateNotificationSettingsValidatorTests
{
    private static UpdateNotificationSettingsRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmailNotificationsEnabled = true,
        ScheduledRemindersEnabled = true,
        Version = 1,
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new UpdateNotificationSettingsValidator();
        Assert.True(validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new UpdateNotificationSettingsValidator();
        var result = validator.Validate(ValidRequest() with { CompanyId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateNotificationSettingsRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Version_Is_Zero()
    {
        var validator = new UpdateNotificationSettingsValidator();
        var result = validator.Validate(ValidRequest() with { Version = 0 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateNotificationSettingsRequest.Version));
    }

    [Fact]
    public void Validate_Fails_When_Version_Is_Negative()
    {
        var validator = new UpdateNotificationSettingsValidator();
        var result = validator.Validate(ValidRequest() with { Version = -1 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateNotificationSettingsRequest.Version));
    }

    [Fact]
    public void Validate_Passes_When_Version_Is_One()
    {
        var validator = new UpdateNotificationSettingsValidator();
        var result = validator.Validate(ValidRequest() with { Version = 1 });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Both_Flags_Are_False()
    {
        var validator = new UpdateNotificationSettingsValidator();
        var result = validator.Validate(ValidRequest() with { EmailNotificationsEnabled = false, ScheduledRemindersEnabled = false });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Only_EmailNotificationsEnabled_Is_True()
    {
        var validator = new UpdateNotificationSettingsValidator();
        var result = validator.Validate(ValidRequest() with { EmailNotificationsEnabled = true, ScheduledRemindersEnabled = false });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Only_ScheduledRemindersEnabled_Is_True()
    {
        var validator = new UpdateNotificationSettingsValidator();
        var result = validator.Validate(ValidRequest() with { EmailNotificationsEnabled = false, ScheduledRemindersEnabled = true });
        Assert.True(result.IsValid);
    }
}
