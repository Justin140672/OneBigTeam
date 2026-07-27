namespace HR.Infrastructure.Abstractions;

// Implemented by HR.Modules.Offboarding (the owning module for offboarding plans). Replays every
// historical offboarding-plan start as an OffboardingStartedIntegrationEvent so that
// HR.Modules.Employees' timeline backfill can populate entries for offboardings that pre-date the
// timeline feature — the same event shape and trigger condition as the live StartOffboarding
// handler (which publishes unconditionally for every OffboardingPlan it creates, regardless of the
// plan's eventual status), just driven from existing data instead of a fresh start.
public interface IOffboardingHistoryReplayer
{
    // Returns the number of historical offboarding-started events replayed (i.e. the number of
    // existing OffboardingPlan rows for the company), so the backfill orchestrator can report an
    // accurate processed count for this source.
    Task<int> ReplayStartedOffboardingsAsync(Guid companyId, CancellationToken cancellationToken);
}
