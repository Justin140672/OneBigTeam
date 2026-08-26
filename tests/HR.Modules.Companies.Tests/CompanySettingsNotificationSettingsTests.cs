using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests;

public class CompanySettingsNotificationSettingsTests
{
    [Fact]
    public void CreateDefault_Sets_Default_NotificationSettings()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.True(settings.EmailNotificationsEnabled);
        Assert.True(settings.ScheduledRemindersEnabled);
    }

    [Fact]
    public void UpdateNotificationSettings_Sets_New_Values()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        settings.UpdateNotificationSettings(false, false, DateTimeOffset.UtcNow);

        Assert.False(settings.EmailNotificationsEnabled);
        Assert.False(settings.ScheduledRemindersEnabled);
    }

    [Fact]
    public void UpdateNotificationSettings_Sets_Only_EmailNotificationsEnabled_False_When_ScheduledRemindersEnabled_Is_True()
    {
        // Covers the negated branch of each independent bool flag.
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        settings.UpdateNotificationSettings(false, true, DateTimeOffset.UtcNow);

        Assert.False(settings.EmailNotificationsEnabled);
        Assert.True(settings.ScheduledRemindersEnabled);
    }

    [Fact]
    public void UpdateNotificationSettings_Sets_Only_ScheduledRemindersEnabled_False_When_EmailNotificationsEnabled_Is_True()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        settings.UpdateNotificationSettings(true, false, DateTimeOffset.UtcNow);

        Assert.True(settings.EmailNotificationsEnabled);
        Assert.False(settings.ScheduledRemindersEnabled);
    }

    [Fact]
    public void UpdateNotificationSettings_Updates_UpdatedAt_And_Bumps_Version()
    {
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), createdAt);
        var versionBefore = settings.Version;
        var updatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        settings.UpdateNotificationSettings(false, true, updatedAt);

        Assert.Equal(updatedAt, settings.UpdatedAt);
        Assert.Equal(versionBefore + 1, settings.Version);
    }
}
