namespace HR.Infrastructure.Abstractions;

/// <summary>
/// SET-06: narrow, read-only projection of the notification-channel fields on Companies'
/// CompanySettings, exposed to HR.Modules.Notifications via <see cref="ICompanyNotificationSettingsReader"/>
/// (mirrors ICompanyRecruitmentSettingsReader/ICompanySicknessSettingsReader).
/// </summary>
public sealed record CompanyNotificationSettings(
    bool EmailNotificationsEnabled,
    bool ScheduledRemindersEnabled)
{
    /// <summary>Backward-compatible defaults for a company with no persisted CompanySettings row yet —
    /// both channels on, preserving pre-SET-06 behaviour.</summary>
    public static readonly CompanyNotificationSettings Default = new(
        EmailNotificationsEnabled: true,
        ScheduledRemindersEnabled: true);
}
