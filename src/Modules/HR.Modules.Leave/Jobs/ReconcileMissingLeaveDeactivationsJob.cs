using Hangfire;
using HR.Modules.Employees.Contracts;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Leave.Jobs;

/// <summary>
/// Gap-2 reliability fix: closes a hole neither <see cref="ReconcileLeavePolicyDeactivationsJob"/>
/// nor Employees' own stranded-departure reconciliation can ever catch. Both
/// <see cref="Features.DeactivateLeavePolicyAssignmentOnEmployeeDeparture.EmployeeDepartureFinalisedHandler"/>
/// itself and the interruption it guards against (process crash) can result in a fully-finalised
/// departure (Employees' EmployeeLeavingProcess.FinalisationCompletedAt is set) with NO
/// corresponding <see cref="LeavePolicyDeactivationOnDeparture"/> row ever having been created —
/// e.g. the handler threw before its own insert (swallowed by
/// HR.SharedKernel.IntegrationEventPublisher, which never blocks the finaliser on a consumer's
/// failure), or the process crashed between the finaliser's save and this handler running at all.
/// <see cref="ReconcileLeavePolicyDeactivationsJob"/> only re-enqueues rows that already exist;
/// Employees' reconciliation only looks for processes where FinalisationCompletedAt is still null,
/// which it is not in this scenario.
///
/// This job authoritatively re-derives "every departure Employees considers fully finalised"
/// (bounded to a lookback window — see <see cref="LookbackDays"/>) via the cross-module
/// <see cref="IFinalisedEmployeeDeparturesReader"/> contract, and for any departure with no
/// LeavePolicyDeactivationOnDeparture row of any status at all, creates one and enqueues
/// <see cref="LeavePolicyDeactivationJob"/> — mirroring
/// EmployeeDepartureFinalisedHandler's own logic (including its "only act if an active assignment
/// exists" guard) exactly, so the behaviour is identical whether the request was created on the
/// normal event-driven path or recovered here.
///
/// Idempotent and safe to run repeatedly/concurrently: the unique index on (company_id,
/// employee_id) means a request already created by the normal path (or a concurrent run of this
/// job) is simply skipped, never duplicated.
/// </summary>
internal sealed class ReconcileMissingLeaveDeactivationsJob(
    LeaveDbContext dbContext,
    IFinalisedEmployeeDeparturesReader finalisedDeparturesReader,
    IClock clock,
    IBackgroundJobClient backgroundJobClient,
    ILogger<ReconcileMissingLeaveDeactivationsJob> logger)
{
    // Bounds the reconciliation scan to recently-finalised departures — this job exists to close a
    // narrow crash/exception window immediately around finalisation, not to be a general-purpose
    // unbounded historical backfill. 30 days comfortably covers any realistic delay between a
    // finalisation succeeding and this daily job's next run(s) picking up the gap.
    private const int LookbackDays = 30;

    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();
        var since = now.AddDays(-LookbackDays);

        var finalisedDepartures = await finalisedDeparturesReader.GetFinalisedDeparturesSinceAsync(
            since, CancellationToken.None);

        if (finalisedDepartures.Count == 0)
            return;

        var employeeIds = finalisedDepartures.Select(d => d.EmployeeId).ToList();

        var existingRequestEmployeeIds = (await dbContext.LeavePolicyDeactivationsOnDeparture
                .Where(d => employeeIds.Contains(d.EmployeeId))
                .Select(d => new { d.CompanyId, d.EmployeeId })
                .ToListAsync())
            .Select(d => (d.CompanyId, d.EmployeeId))
            .ToHashSet();

        var missing = finalisedDepartures
            .Where(d => !existingRequestEmployeeIds.Contains((d.CompanyId, d.EmployeeId)))
            .ToList();

        if (missing.Count == 0)
            return;

        var createdCount = 0;

        foreach (var departure in missing)
        {
            try
            {
                var assignment = await dbContext.EmployeeLeavePolicyAssignments
                    .FirstOrDefaultAsync(
                        a => a.CompanyId == departure.CompanyId && a.EmployeeId == departure.EmployeeId);

                // Mirrors EmployeeDepartureFinalisedHandler's own guard: nothing to deactivate.
                if (assignment is null || !assignment.IsActive)
                    continue;

                // Re-check immediately before insert — the unique index is the final backstop
                // against a concurrent run/normal-path request racing us, but avoiding the
                // known-doomed insert attempt keeps the log clean.
                var alreadyRequested = await dbContext.LeavePolicyDeactivationsOnDeparture
                    .AnyAsync(d => d.CompanyId == departure.CompanyId && d.EmployeeId == departure.EmployeeId);
                if (alreadyRequested)
                    continue;

                var request = LeavePolicyDeactivationOnDeparture.CreatePending(
                    Guid.NewGuid(), departure.CompanyId, departure.EmployeeId, departure.FinalisationCompletedAt, now);

                dbContext.LeavePolicyDeactivationsOnDeparture.Add(request);
                await dbContext.SaveChangesAsync();

                backgroundJobClient.Enqueue<LeavePolicyDeactivationJob>(
                    job => job.ProcessAsync(request.Id, request.CompanyId));

                createdCount++;
            }
            catch (DbUpdateException ex)
            {
                // Backstop for the race the pre-check above narrows but cannot fully close —
                // another run/the normal event-driven path won the insert first.
                logger.LogWarning(
                    ex,
                    "ReconcileMissingLeaveDeactivationsJob: concurrent creation detected for employee {EmployeeId} (company {CompanyId}) — skipping.",
                    departure.EmployeeId, departure.CompanyId);

                foreach (var entry in dbContext.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
                    entry.State = EntityState.Detached;
            }
        }

        if (createdCount > 0)
        {
            logger.LogWarning(
                "ReconcileMissingLeaveDeactivationsJob: recovered {Count} finalised departure(s) with no leave policy deactivation request — created and enqueued.",
                createdCount);
        }
    }
}
