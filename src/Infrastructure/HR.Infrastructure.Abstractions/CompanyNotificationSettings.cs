namespace HR.Infrastructure.Abstractions;

public sealed record CompanyNotificationSettings(
    bool EmailNotificationsEnabled,
    bool ScheduledRemindersEnabled)
{
    public static readonly CompanyNotificationSettings Default = new(
        EmailNotificationsEnabled: true,
        ScheduledRemindersEnabled: true);
}
