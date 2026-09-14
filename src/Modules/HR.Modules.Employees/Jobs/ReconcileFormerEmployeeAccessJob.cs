using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Employees.Jobs;

// P1 fix (departure access disablement): republishes EmployeeDepartureFinalisedIntegrationEvent
// for former employees whose access was already recorded as disabled here (HasSystemAccess=false)
// before HR.Modules.Identity started consuming that event to actually disable the linked
// ApplicationUser (see Identity's Features/OnEmployeeDepartureFinalised/Handler.cs). Without this
// sweep, employees whose departure was finalised prior to that fix would keep an active
// application account forever, since nothing else would ever re-trigger disablement for them.
//
// Marks AccessDisablementReconciledAt so each employee is only ever swept once, regardless of
// whether Identity's own disablement subsequently succeeds — downstream reliability (retry,
// visibility of a failed disablement) is entirely owned by Identity's own durable
// AccountDisablement record/job, not by this one-off publish. Safe to run repeatedly/concurrently:
// re-publishing for an employee Identity has already processed is a no-op there too (idempotent
// unique constraint on ApplicationUserId).
internal sealed class ReconcileFormerEmployeeAccessJob(
    EmployeesDbContext dbContext,
    IIntegrationEventPublisher integrationEventPublisher,
    IClock clock,
    ILogger<ReconcileFormerEmployeeAccessJob> logger)
{
    public async Task ExecuteAsync()
    {
        var candidates = await dbContext.Employees
            .Where(e =>
                e.Status == EmploymentStatus.FormerEmployee &&
                !e.HasSystemAccess &&
                e.AccessDisablementReconciledAt == null)
            .ToListAsync();

        if (candidates.Count == 0)
            return;

        var now = clock.UtcNowOffset();

        foreach (var employee in candidates)
        {
            await integrationEventPublisher.PublishAsync(
                new EmployeeDepartureFinalisedIntegrationEvent(
                    employee.CompanyId,
                    employee.Id,
                    employee.LeavingDate ?? DateOnly.FromDateTime(employee.UpdatedAt.Date),
                    now,
                    AccessDisabled: true),
                CancellationToken.None);

            employee.MarkAccessDisablementReconciled(now);
        }

        await dbContext.SaveChangesAsync();

        logger.LogInformation(
            "ReconcileFormerEmployeeAccessJob: republished departure-finalised access disablement for {Count} former employee(s).",
            candidates.Count);
    }
}
