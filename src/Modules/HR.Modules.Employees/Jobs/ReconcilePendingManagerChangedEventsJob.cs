using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Employees.Jobs;

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
