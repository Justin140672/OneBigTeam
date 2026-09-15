using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Employees.Jobs;

/// <summary>
/// Daily sweep that publishes any PendingManagerChangedEvent still missing PublishedAt — recovers
/// EmployeeManagerChangedIntegrationEvent deliveries lost when EmployeeDepartureFinalizer's
/// manager-departure cascade (CascadeManagerDepartureAsync) was interrupted after the report's
/// ManagerId reassignment was saved but before (or partway through) publishing. See
/// PendingManagerChangedEvent's remarks for why the event can't simply be re-derived from the
/// report's current state on retry.
///
/// Safe to run repeatedly/concurrently: MarkPublished is a no-op once already set, and
/// EmployeeManagerChangedIntegrationEvent's consumers (e.g. Probation's ManagerChangedHandler) are
/// themselves expected to be idempotent against redelivery.
/// </summary>
internal sealed class ReconcilePendingManagerChangedEventsJob(
    EmployeesDbContext dbContext,
    IIntegrationEventPublisher integrationEventPublisher,
    IClock clock,
    ILogger<ReconcilePendingManagerChangedEventsJob> logger)
{
    public async Task ExecuteAsync()
    {
        var pending = await dbContext.PendingManagerChangedEvents
            .Where(e => e.PublishedAt == null)
            .ToListAsync();

        if (pending.Count == 0)
            return;

        var now = clock.UtcNowOffset();

        foreach (var pendingEvent in pending)
        {
            try
            {
                // Gap-1 reliability fix: use PublishAndConfirmAsync so a still-failing required
                // consumer (Probation's ManagerChangedHandler) keeps this record Pending for the
                // next sweep, instead of PublishAsync's swallow-and-log behaviour marking it
                // published regardless of whether the required consumer actually succeeded.
                var confirmed = await integrationEventPublisher.PublishAndConfirmAsync(
                    new EmployeeManagerChangedIntegrationEvent(
                        pendingEvent.CompanyId, pendingEvent.ReportEmployeeId, pendingEvent.PreviousManagerId,
                        pendingEvent.NewManagerId, pendingEvent.OccurredAt),
                    CancellationToken.None);

                if (confirmed)
                {
                    pendingEvent.MarkPublished(now);
                    await dbContext.SaveChangesAsync();
                }
                else
                {
                    // A required consumer failed (PublishAndConfirmAsync already logged the
                    // underlying exception per-handler) — log at this job's own level too so the
                    // record's identifiers are captured, matching the logging this job already did
                    // for an outright exception below. Left Pending for the next scheduled run.
                    logger.LogError(
                        "ReconcilePendingManagerChangedEventsJob: a required consumer failed to apply pending manager-changed event {PendingEventId} for report {ReportEmployeeId} (company {CompanyId}, leaving process {LeavingProcessId}) — will retry on next run.",
                        pendingEvent.Id, pendingEvent.ReportEmployeeId, pendingEvent.CompanyId, pendingEvent.LeavingProcessId);
                }
            }
            catch (Exception ex)
            {
                // One report's failed recovery must not block the rest of the sweep — left Pending
                // for the next scheduled run.
                logger.LogError(
                    ex,
                    "ReconcilePendingManagerChangedEventsJob: failed to republish pending manager-changed event {PendingEventId} for report {ReportEmployeeId} (company {CompanyId}, leaving process {LeavingProcessId}) — will retry on next run.",
                    pendingEvent.Id, pendingEvent.ReportEmployeeId, pendingEvent.CompanyId, pendingEvent.LeavingProcessId);

                // Discard only entries left dirty by this failed attempt — a blanket
                // ChangeTracker.Clear() would also detach the remaining not-yet-processed pending
                // events already loaded above (still Unchanged), silently dropping their later
                // MarkPublished/SaveChangesAsync work for the rest of this run.
                foreach (var entry in dbContext.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
                {
                    entry.State = EntityState.Detached;
                }
            }
        }

        logger.LogInformation(
            "ReconcilePendingManagerChangedEventsJob: processed {Count} pending manager-changed event(s).",
            pending.Count);
    }
}
