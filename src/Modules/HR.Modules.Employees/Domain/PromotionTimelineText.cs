namespace HR.Modules.Employees.Domain;

/// <summary>
/// Title and description of the employee-timeline entry for a promotion. Shared by the eager entry
/// written when a future-dated promotion is recorded and the entry written when it is finalised, so
/// both describe the change identically (they dedupe on EventType + SourceRecordId).
/// </summary>
internal static class PromotionTimelineText
{
    public static (string Title, string Description) Describe(
        bool isInternalAppointment, string previousTitle, string newTitle) =>
        isInternalAppointment
            ? ("Internal appointment", $"Appointed from {previousTitle} to {newTitle} through an internal vacancy.")
            : ("Promoted", $"Promoted from {previousTitle} to {newTitle}.");
}
