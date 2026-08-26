namespace HR.Modules.Companies.Features.UpdateNotificationSettings;

internal sealed record UpdateNotificationSettingsResponse(
    Guid CompanyId,
    bool EmailNotificationsEnabled,
    bool ScheduledRemindersEnabled,
    DateTimeOffset UpdatedAt,
    int Version);
