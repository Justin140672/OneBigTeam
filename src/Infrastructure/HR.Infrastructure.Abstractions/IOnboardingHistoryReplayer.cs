namespace HR.Infrastructure.Abstractions;

// Implemented by HR.Modules.Onboarding (the owning module for onboarding plans). Replays every
// historical onboarding-plan completion as an OnboardingCompletedIntegrationEvent so that
// HR.Modules.Employees' timeline backfill can populate entries for plans that pre-date the
// timeline feature — the same event shape and trigger condition as the live
// CompleteOnboardingTaskFromTaskAction, just driven from existing data instead of a fresh
// completion.
public interface IOnboardingHistoryReplayer
{
    // Returns the number of historical onboarding-completed events replayed, so the backfill
    // orchestrator can report an accurate processed count for this source.
    Task<int> ReplayOnboardingCompletedAsync(Guid companyId, CancellationToken cancellationToken);
}
