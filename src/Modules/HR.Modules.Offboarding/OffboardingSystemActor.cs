namespace HR.Modules.Offboarding;

// OFF-03: well-known actor id used for every Tasks-module TaskItem generated automatically by an
// offboarding plan (asset returns, document review, manager exit checklist). Distinguishes
// system-generated work from a human actor, and specifically avoids identifying the leaving
// employee themselves as the "creator" of their own exit tasks. Mirrors the same well-known-guid
// convention already established elsewhere in this session (ProbationSystemActor.Id,
// FitNoteEvidenceRequestService.SystemActorId) rather than inventing a third pattern.
internal static class OffboardingSystemActor
{
    public static readonly Guid Id = Guid.Empty;
}
