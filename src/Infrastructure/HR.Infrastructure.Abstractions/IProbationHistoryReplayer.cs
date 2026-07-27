namespace HR.Infrastructure.Abstractions;

// Implemented by HR.Modules.Probation (the owning module for probation records). Replays every
// historical "probation passed" outcome as a ProbationPassedIntegrationEvent so that
// HR.Modules.Employees' timeline backfill can populate entries for records that pre-date the
// timeline feature — the same event shape and trigger condition as the live
// CompleteProbationReview handler, just driven from existing data instead of a fresh decision.
public interface IProbationHistoryReplayer
{
    // Returns the number of historical probation-passed events replayed, so the backfill
    // orchestrator can report an accurate processed count for this source.
    Task<int> ReplayProbationPassedAsync(Guid companyId, CancellationToken cancellationToken);
}
