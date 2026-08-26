using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests;

public class CompanySettingsDocumentReminderSettingsTests
{
    [Fact]
    public void CreateDefault_Sets_Default_Document_Reminder_Schedule_To_Enabled_90_30_7()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.True(settings.DocumentRemindersEnabled);
        Assert.Equal(90, settings.DocumentReminderOffsetDays1);
        Assert.Equal(30, settings.DocumentReminderOffsetDays2);
        Assert.Equal(7, settings.DocumentReminderOffsetDays3);
    }

    [Fact]
    public void UpdateDocumentReminderSettings_Sets_New_Values()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        settings.UpdateDocumentReminderSettings(true, 60, 21, 3, DateTimeOffset.UtcNow);

        Assert.True(settings.DocumentRemindersEnabled);
        Assert.Equal(60, settings.DocumentReminderOffsetDays1);
        Assert.Equal(21, settings.DocumentReminderOffsetDays2);
        Assert.Equal(3, settings.DocumentReminderOffsetDays3);
    }

    [Fact]
    public void UpdateDocumentReminderSettings_Allows_Disabling_Reminders()
    {
        // Negated branch of the enabled flag.
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        settings.UpdateDocumentReminderSettings(false, null, null, null, DateTimeOffset.UtcNow);

        Assert.False(settings.DocumentRemindersEnabled);
        Assert.Null(settings.DocumentReminderOffsetDays1);
        Assert.Null(settings.DocumentReminderOffsetDays2);
        Assert.Null(settings.DocumentReminderOffsetDays3);
    }

    [Fact]
    public void UpdateDocumentReminderSettings_Allows_Partial_Schedule_With_Some_Slots_Null()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        settings.UpdateDocumentReminderSettings(true, 90, null, 7, DateTimeOffset.UtcNow);

        Assert.True(settings.DocumentRemindersEnabled);
        Assert.Equal(90, settings.DocumentReminderOffsetDays1);
        Assert.Null(settings.DocumentReminderOffsetDays2);
        Assert.Equal(7, settings.DocumentReminderOffsetDays3);
    }

    [Fact]
    public void UpdateDocumentReminderSettings_Updates_UpdatedAt_And_Bumps_Version()
    {
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), createdAt);
        var versionBefore = settings.Version;
        var updatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        settings.UpdateDocumentReminderSettings(false, null, null, null, updatedAt);

        Assert.Equal(updatedAt, settings.UpdatedAt);
        Assert.Equal(versionBefore + 1, settings.Version);
    }
}
