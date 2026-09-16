using Hangfire;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Jobs;

/// <summary>
/// Ticket 10 (P1) follow-up / Ticket 19 (P2): recovers <see cref="AccountDisablement"/> requests
/// that never made (or never finished) their trip through <see cref="AccountDisablementJob"/> —
/// e.g. a process interruption between OnEmployeeDepartureFinalised's SaveChangesAsync and its
/// backgroundJobClient.Enqueue call, an AccountDisablementJob run stuck in Processing after a crash,
/// or a non-terminal Failed record left for another automatic attempt.
///
/// Idempotent by design: AccountDisablementJob itself no-ops on an already-Processed record, and
/// its own claim (see AccountDisablementJob's remarks) is what makes a live dispatch and this
/// sweep's re-enqueue for the SAME row mutually exclusive — re-enqueuing a request that actually
/// did complete (but whose Processing/Processed transition hadn't landed yet when this swept it)
/// never duplicates the disablement effect.
///
/// Ticket 19 (P2): a stale-Processing row is now identified by its <see cref="AccountDisablement.LeaseExpiresAt"/>
/// having actually elapsed — the authoritative signal a real lease provides — rather than a fixed
/// "no progress within N minutes" age heuristic substituting for one.
///
/// Ticket 19 (P2) — atomic claim, not a bare status reset: this sweep claims each eligible row
/// directly via <see cref="AccountDisablement.Claim"/> (straight to Processing, under this sweep's
/// own instance id), guarded by the SAME optimistic-concurrency check AccountDisablementJob's own
/// claim uses. An earlier version merely reset eligible rows back to Pending before enqueuing —
/// but a plain Pending row is indistinguishable from any other untouched Pending row, so a second
/// reconciler sweep (or a concurrently-running live dispatch) could see it as "still eligible" and
/// ALSO claim-and-enqueue it, even though the first claim's own optimistic-concurrency guard
/// technically "succeeded" (nothing conflicted, because neither one had actually taken ownership).
/// Going straight to Processing+claimed closes that gap: the row is no longer Pending (so it no
/// longer matches ANY reconciler's Pending criterion), and only one reconciler's guarded save can
/// ever win a given row's version, so two replicas racing the same stale row can never both
/// claim-and-enqueue it — the loser's DbUpdateConcurrencyException is caught and simply skips that
/// row this sweep. A terminally-failed record (see <see cref="AccountDisablement.IsTerminallyFailed"/>)
/// is never reset here — it requires an explicit <see cref="AccountDisablement.RecordManualRetry"/>.
/// </summary>
internal sealed class AccountDisablementReconciliationJob(
    IdentityDbContext db,
    IClock clock,
    IBackgroundJobClient backgroundJobClient,
    ILogger<AccountDisablementReconciliationJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        var now = clock.UtcNow;
        var reconcilerInstanceId = Guid.NewGuid();

        var candidates = await db.AccountDisablements
            .Where(d =>
                d.Status == AccountDisablement.StatusPending
                || (d.Status == AccountDisablement.StatusFailed && !d.IsTerminallyFailed)
                || (d.Status == AccountDisablement.StatusProcessing
                    && (d.LeaseExpiresAt == null || d.LeaseExpiresAt < now)))
            .ToListAsync();

        var claimed = new List<AccountDisablement>();

        foreach (var request in candidates)
        {
            var expectedVersion = request.Version;
            var reason = request.Status;

            if (request.Status == AccountDisablement.StatusFailed)
            {
                // Domain guard: refuses unless still Failed (matches what was just queried) — makes
                // the intent explicit even though Claim() below would work from any status.
                request.ResetForRetry();
            }

            // Ticket 19 (P2): claims the row directly — Pending, non-terminal Failed (just reset
            // above), and stale-Processing all end up Processing+claimed-by-this-sweep in the SAME
            // guarded save, never left sitting as a bare, still-"eligible" Pending row in between.
            request.Claim(reconcilerInstanceId, now);

            var result = await db.SaveChangesWithConcurrencyAsync(
                request, expectedVersion,
                "This account disablement was already claimed by another reconciler.",
                CancellationToken.None);

            if (result.IsFailure)
            {
                logger.LogInformation(
                    "AccountDisablementReconciliationJob: lost the claim race for account disablement {AccountDisablementId} (company {CompanyId}) — skipping this sweep.",
                    request.Id, request.CompanyId);
                continue;
            }

            claimed.Add(request);

            logger.LogWarning(
                "AccountDisablementReconciliationJob: claimed and enqueuing {Reason} account disablement {AccountDisablementId} (company {CompanyId}) for employee {EmployeeId}.",
                reason, request.Id, request.CompanyId, request.EmployeeId);
        }

        foreach (var request in claimed)
        {
            backgroundJobClient.Enqueue<AccountDisablementJob>(
                job => job.ProcessAsync(request.Id, request.CompanyId, reconcilerInstanceId));
        }
    }
}
