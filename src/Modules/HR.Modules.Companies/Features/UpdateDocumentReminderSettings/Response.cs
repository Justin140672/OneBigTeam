namespace HR.Modules.Companies.Features.UpdateDocumentReminderSettings;

internal sealed record UpdateDocumentReminderSettingsResponse(
    Guid CompanyId,
    bool RemindersEnabled,
    int? OffsetDays1,
    int? OffsetDays2,
    int? OffsetDays3,
    DateTimeOffset UpdatedAt,
    int Version);
