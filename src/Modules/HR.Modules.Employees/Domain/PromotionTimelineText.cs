namespace HR.Modules.Employees.Domain;

internal static class PromotionTimelineText
{
    public static (string Title, string Description) Describe(
        bool isInternalAppointment, string previousTitle, string newTitle) =>
        isInternalAppointment
            ? ("Internal appointment", $"Appointed from {previousTitle} to {newTitle} through an internal vacancy.")
            : ("Promoted", $"Promoted from {previousTitle} to {newTitle}.");
}
