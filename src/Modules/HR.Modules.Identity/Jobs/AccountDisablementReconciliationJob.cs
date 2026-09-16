using Hangfire;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Jobs;

/// <summary>
/// Ticket 10 (P1) follow-up: recovers <see cref="AccountDisablement"/> requests that never made
/// (or never finished) their trip through <see cref="AccountDisablementJob"/> — e.g. a process
/// interruption between OnEmployeeDepartureFinalised's SaveChangesAsync and its
/// backgroundJobClient.Enqueue call, an AccountDisablementJob run stuck in Processing after a crash,
/// or a Failed record left for manual attention that has since aged past its own retry window.
///
/// Idempotent by design: AccountDisablementJob itself no-ops on an already-Processed record, so
/// re-enqueuing a request that actually did complete (but whose Processing/Processed transition
/// hadn't landed yet when this swept it) never duplicates the disablement effect.
/// </summary>
internal sealed class AccountDisablementReconciliationJob(
    IdentityDbContext db,
    IClock clock,
    IBackgroundJobClient backgroundJobClient,
    ILogger<AccountDisablementReconciliationJob> logger)
{
    /// <summary>
    /// A Processing record older than this is assumed to belong to an interrupted/crashed attempt
    /// rather than one genuinely still in flight (AccountDisablementJob itself completes in
    /// milliseconds), so it's safe to reset and retry.
    /// </summary>
    private static readonly TimeSpan StuckProcessingThreshold = TimeSpan.FromMinutes(15);

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        var now = clock.UtcNow;
        var cutoff = now - StuckProcessingThreshold;

        var stale = await db.AccountDisablements
            .Where(d =>
                d.Status == AccountDisablement.StatusPending
                || d.Status == AccountDisablement.StatusFailed
                || (d.Status == AccountDisablement.StatusProcessing
                    && (d.LastAttemptAt == null || d.LastAttemptAt < cutoff)))
            .ToListAsync();

        foreach (var request in stale)
        {
            if (request.Status == AccountDisablement.StatusFailed)
            {
                request.ResetForRetry();
            }
            else if (request.Status == AccountDisablement.StatusProcessing)
            {
                // Force it back to a re-enqueueable state; AccountDisablementJob's own
                // MarkProcessing call will move it forward again on the next attempt.
                request.ResetToPendingAfterInterruption();
            }

            logger.LogWarning(
                "AccountDisablementReconciliationJob: re-enqueuing stale account disablement {AccountDisablementId} (company {CompanyId}) for employee {EmployeeId}.",
                request.Id, request.CompanyId, request.EmployeeId);
        }

        if (stale.Count > 0)
        {
            await db.SaveChangesAsync();
        }

        foreach (var request in stale)
        {
            backgroundJobClient.Enqueue<AccountDisablementJob>(
                job => job.ProcessAsync(request.Id, request.CompanyId));
        }
    }
}
