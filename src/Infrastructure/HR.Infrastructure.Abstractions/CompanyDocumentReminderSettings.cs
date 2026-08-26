namespace HR.Infrastructure.Abstractions;

/// <summary>
/// SET-07: narrow, read-only projection of the document expiry reminder schedule on Companies'
/// CompanySettings, exposed to HR.Modules.Documents via <see cref="ICompanyDocumentReminderSettingsReader"/>.
/// Mirrors the nullable-triple-column pattern already used for probation checkpoints
/// (ProbationCheckpointDay1/2/3) — a fixed set of up to 3 configurable day-offsets, ordered
/// furthest-out first. A null slot means that reminder stage is not configured/disabled; slots must
/// be positive, unique and strictly decreasing (Day1 &gt; Day2 &gt; Day3) wherever both are set —
/// enforced by UpdateDocumentReminderSettingsValidator, not here.
/// </summary>
public sealed record CompanyDocumentReminderSettings(
    bool RemindersEnabled,
    int? OffsetDays1,
    int? OffsetDays2,
    int? OffsetDays3)
{
    /// <summary>Backward-compatible defaults for a company with no persisted CompanySettings row yet —
    /// the standard 90/30/7-day schedule, reminders enabled.</summary>
    public static readonly CompanyDocumentReminderSettings Default = new(
        RemindersEnabled: true,
        OffsetDays1: 90,
        OffsetDays2: 30,
        OffsetDays3: 7);
}
