namespace HR.Modules.Companies.Features.UpdateNotificationSettings;

internal sealed record UpdateNotificationSettingsRequest
{
    public Guid CompanyId { get; init; }
    public bool EmailNotificationsEnabled { get; init; } = true;
    public bool ScheduledRemindersEnabled { get; init; } = true;

    public int Version { get; init; }
}
