using Hangfire;
using HR.Modules.Employees.Contracts;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Leave.Jobs;

/// <summary>
/// Round 3 reliability fix (Gap-2 follow-up): <see cref="ReconcileMissingLeaveDeactivationsJob"/>'s
/// 30-day lookback exists to bound that job's regular per-run cost, but it means a stranded departure
/// finalised more than 30 days ago (an extended outage of the recovery mechanism, or a departure
/// stranded before this whole recovery mechanism existed) is invisible to it forever. This job is the
/// distinct, bounded, paginated historical backlog sweep that closes that gap exactly once — it walks
/// the ENTIRE finalised-departure history via the keyset-paginated
/// <see cref="IFinalisedEmployeeDeparturesReader.GetFinalisedDeparturesPageAsync"/>, in small bounded
/// batches per run, resuming from a durably persisted cursor
/// (<see cref="HistoricalLeaveDeactivationRepairProgress"/>) so an interrupted sweep picks up roughly
/// where it left off instead of restarting.
///
/// Deliberately NOT a replacement for <see cref="ReconcileMissingLeaveDeactivationsJob"/>: that job
/// keeps running daily forever, covering new departures going forward with its narrow, cheap 30-day
/// window. This job runs until <see cref="HistoricalLeaveDeactivationRepairProgress.IsComplete"/>, then
/// becomes a permanent no-op — a one-time backlog drain, not an ongoing duplicate of the daily job.
///
/// Rehire safety: before creating a deactivation request from a potentially very old departure record,
/// this job checks <see cref="ICurrentEmployeeReader"/> (the same cross-module contract Leave already
/// uses elsewhere to filter to non-former employees) — if the employee is a CURRENT (non-former)
/// employee again, the departure being repaired is stale relative to a rehire, and deactivating their
/// present-day leave policy assignment based on it would be wrong. That employee is skipped entirely.
///
/// Idempotent/duplicate-safe: mirrors ReconcileMissingLeaveDeactivationsJob's own pre-check-then-insert
/// pattern, backed by the same unique index on (company_id, employee_id) on
/// <see cref="LeavePolicyDeactivationOnDeparture"/> as the final backstop against a race with the
/// normal event-driven path, a concurrent run, or the daily reconciliation job.
/// </summary>
internal sealed class ReconcileHistoricalLeaveDeactivationsJob(
    LeaveDbContext dbContext,
    IFinalisedEmployeeDeparturesReader finalisedDeparturesReader,
    ICurrentEmployeeReader currentEmployeeReader,
    IClock clock,
    IBackgroundJobClient backgroundJobClient,
    ILogger<ReconcileHistoricalLeaveDeactivationsJob> logger)
{
    // Bounds per-page and per-run cost — this sweep must never issue one unbounded query over the
    // entire departure history. At BatchSize=200 and MaxBatchesPerRun=5, each run processes at most
    // 1,000 historical departures; a large backlog is drained over several scheduled runs rather than
    // in one long-running pass.
    private const int BatchSize = 200;
    private const int MaxBatchesPerRun = 5;

    public async Task ExecuteAsync()
    {
        var progress = await dbContext.HistoricalLeaveDeactivationRepairProgress
            .FirstOrDefaultAsync(p => p.Id == HistoricalLeaveDeactivationRepairProgress.SingletonId);

        var now = clock.UtcNowOffset();

        if (progress is null)
        {
            progress = HistoricalLeaveDeactivationRepairProgress.CreateNew(now);
            dbContext.HistoricalLeaveDeactivationRepairProgress.Add(progress);
            await dbContext.SaveChangesAsync();
        }

        if (progress.IsComplete)
            return; // One-time historical sweep already finished — permanent no-op from here on.

        var currentEmployeeIdsByCompany = new Dictionary<Guid, HashSet<Guid>>();
        var totalRepairedThisRun = 0;

        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
        {
            var page = await finalisedDeparturesReader.GetFinalisedDeparturesPageAsync(
                progress.LastProcessedFinalisationCompletedAt,
                progress.LastProcessedEmployeeId,
                BatchSize,
                CancellationToken.None);

            if (page.Count == 0)
            {
                progress.MarkComplete(now);
                await dbContext.SaveChangesAsync();

                logger.LogInformation(
                    "ReconcileHistoricalLeaveDeactivationsJob: historical sweep complete. Total repaired across all runs: {TotalRepaired}.",
                    progress.TotalRepaired);
                return;
            }

            var repairedInBatch = 0;

            foreach (var departure in page)
            {
                try
                {
                    if (!currentEmployeeIdsByCompany.TryGetValue(departure.CompanyId, out var currentIds))
                    {
                        var ids = await currentEmployeeReader.GetCurrentEmployeeIdsAsync(departure.CompanyId, CancellationToken.None);
                        currentIds = ids.ToHashSet();
                        currentEmployeeIdsByCompany[departure.CompanyId] = currentIds;
                    }

                    // Rehire safety: this employee is a current (non-former) employee again, so a
                    // leave-policy assignment found active today may be a legitimate post-rehire
                    // assignment — never deactivate it based on a stale historical departure record.
                    if (currentIds.Contains(departure.EmployeeId))
                        continue;

                    var alreadyRequested = await dbContext.LeavePolicyDeactivationsOnDeparture
                        .AnyAsync(d => d.CompanyId == departure.CompanyId && d.EmployeeId == departure.EmployeeId);
                    if (alreadyRequested)
                        continue;

                    var assignment = await dbContext.EmployeeLeavePolicyAssignments
                        .FirstOrDefaultAsync(
                            a => a.CompanyId == departure.CompanyId && a.EmployeeId == departure.EmployeeId);

                    // Mirrors EmployeeDepartureFinalisedHandler's/ReconcileMissingLeaveDeactivationsJob's
                    // own guard: nothing to deactivate.
                    if (assignment is null || !assignment.IsActive)
                        continue;

                    var request = LeavePolicyDeactivationOnDeparture.CreatePending(
                        Guid.NewGuid(), departure.CompanyId, departure.EmployeeId, departure.FinalisationCompletedAt, now);

                    dbContext.LeavePolicyDeactivationsOnDeparture.Add(request);
                    await dbContext.SaveChangesAsync();

                    backgroundJobClient.Enqueue<LeavePolicyDeactivationJob>(
                        job => job.ProcessAsync(request.Id, request.CompanyId));

                    repairedInBatch++;
                }
                catch (DbUpdateException ex)
                {
                    // Backstop for the race the pre-check above narrows but cannot fully close —
                    // another run/the normal event-driven path/the daily reconciliation job won the
                    // insert first. Logged (not silently skipped) per the requirement that outstanding
                    // failures be recorded, even though this specific race is benign.
                    logger.LogWarning(
                        ex,
                        "ReconcileHistoricalLeaveDeactivationsJob: concurrent creation detected for employee {EmployeeId} (company {CompanyId}) — skipping.",
                        departure.EmployeeId, departure.CompanyId);

                    foreach (var entry in dbContext.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
                        entry.State = EntityState.Detached;
                }
                catch (Exception ex)
                {
                    // Any other failure for this one departure must not abort the rest of the batch or
                    // lose the cursor's forward progress on the departures already handled — logged
                    // explicitly with enough identifiers to investigate and re-run/repair by hand if
                    // needed, then the sweep continues.
                    logger.LogError(
                        ex,
                        "ReconcileHistoricalLeaveDeactivationsJob: failed to repair historical departure for employee {EmployeeId} (company {CompanyId}, finalised {FinalisationCompletedAt:u}) — leaving unresolved for manual follow-up.",
                        departure.EmployeeId, departure.CompanyId, departure.FinalisationCompletedAt);

                    foreach (var entry in dbContext.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
                        entry.State = EntityState.Detached;
                }
            }

            var last = page[^1];
            progress.Advance(last.FinalisationCompletedAt, last.EmployeeId, repairedInBatch, now);
            await dbContext.SaveChangesAsync();

            totalRepairedThisRun += repairedInBatch;

            if (page.Count < BatchSize)
            {
                // Reached the end of the history on this page — mark complete now rather than waiting
                // for an extra run that would just find an empty page.
                progress.MarkComplete(now);
                await dbContext.SaveChangesAsync();
                break;
            }
        }

        if (totalRepairedThisRun > 0)
        {
            logger.LogWarning(
                "ReconcileHistoricalLeaveDeactivationsJob: repaired {Count} historical departure(s) with no leave policy deactivation request in this run.",
                totalRepairedThisRun);
        }
    }
}
