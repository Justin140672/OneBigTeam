namespace HR.Modules.Companies.Features.UpdateDocumentReminderSettings;

internal sealed record UpdateDocumentReminderSettingsRequest
{
    public Guid CompanyId { get; init; }
    public bool RemindersEnabled { get; init; } = true;
    public int? OffsetDays1 { get; init; } = 90;
    public int? OffsetDays2 { get; init; } = 30;
    public int? OffsetDays3 { get; init; } = 7;

    public int Version { get; init; }
}
