namespace HR.Infrastructure.Abstractions;

public sealed record CompanyDocumentReminderSettings(
    bool RemindersEnabled,
    int? OffsetDays1,
    int? OffsetDays2,
    int? OffsetDays3)
{
    public static readonly CompanyDocumentReminderSettings Default = new(
        RemindersEnabled: true,
        OffsetDays1: 90,
        OffsetDays2: 30,
        OffsetDays3: 7);
}
