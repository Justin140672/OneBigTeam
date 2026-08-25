namespace HR.Modules.Notifications;

// NOT-05: well-known actor id used for audit events raised by shared notification infrastructure
// (NotificationWriter, EmailDeliveryJob) that has no natural human actor at its call site — a
// notification's creation is usually a side effect of some other module's business handler
// (leave approved, task assigned, etc.), and email delivery is entirely background/job-driven.
// Mirrors the same well-known-guid convention already established elsewhere in this session
// (OffboardingSystemActor.Id, ProbationSystemActor.Id, FitNoteEvidenceRequestService.SystemActorId)
// rather than inventing a fourth pattern.
internal static class NotificationsSystemActor
{
    public static readonly Guid Id = Guid.Empty;
}
