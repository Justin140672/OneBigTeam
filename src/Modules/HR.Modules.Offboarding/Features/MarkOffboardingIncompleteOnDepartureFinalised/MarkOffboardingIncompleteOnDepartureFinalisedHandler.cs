using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Features.MarkOffboardingIncompleteOnDepartureFinalised;

/// <summary>
/// OFF-07: consumes <c>EmployeeDepartureFinalisedIntegrationEvent</c> (published by Employees'
/// EmployeeDepartureFinalizer once an employee's leaving date has actually been reached/confirmed)
/// and, if this employee's most recent offboarding plan still has unresolved mandatory tasks at
/// that moment, raises a persistent, queryable HR exception on the plan
/// (<see cref="OffboardingPlan.HasIncompleteOffboardingAtDeparture"/>) rather than relying solely on
/// EmployeeDepartureFinalizer's existing one-time manager notification
/// (<c>NotificationType.IncompleteOffboardingAtDeparture</c>), which the manager can miss or dismiss
/// and which leaves no durable trace once read. This flag stays visible/queryable (see
/// OffboardingPlanConfiguration's company_id/HasIncompleteOffboardingAtDeparture index) until HR
/// resolves it by finishing the plan through the normal completion path.
///
/// Deliberately event-driven rather than a direct call from Employees, per module-boundary rules —
/// Employees must never reference Offboarding's implementation project directly. Idempotent: a plan
/// already flagged is left untouched by MarkIncompleteOffboardingAtDeparture, so redelivery of this
/// event is always a safe no-op.
/// </summary>
internal sealed class MarkOffboardingIncompleteOnDepartureFinalisedHandler(
    OffboardingDbContext dbContext,
    IClock clock,
    IHrAdministratorDirectory hrAdministratorDirectory,
    INotificationWriter notificationWriter,
    IAuditEventPublisher auditPublisher)
    : IIntegrationEventHandler<EmployeeDepartureFinalisedIntegrationEvent>
{
    public async Task HandleAsync(
        EmployeeDepartureFinalisedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var plan = await dbContext.OffboardingPlans
            .Where(p => p.CompanyId == integrationEvent.CompanyId && p.EmployeeId == integrationEvent.EmployeeId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        // No plan at all, or the most recent plan already Completed/Cancelled: nothing outstanding
        // to flag. A Cancelled plan represents a withdrawn leaving process, not an incomplete one.
        if (plan is null || plan.Status is OffboardingStatus.Completed or OffboardingStatus.Cancelled)
            return;

        var tasks = await dbContext.OffboardingTasks
            .Where(t => t.OffboardingPlanId == plan.Id)
            .ToListAsync(cancellationToken);

        if (OffboardingPlan.CanComplete(tasks))
            return; // Every mandatory task is already resolved — nothing incomplete to flag.

        var now = clock.UtcNowOffset();
        var wasAlreadyFlagged = plan.HasIncompleteOffboardingAtDeparture;

        plan.MarkIncompleteOffboardingAtDeparture(now);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (wasAlreadyFlagged)
            return;

        await auditPublisher.PublishAsync(
            new OffboardingIncompleteAtDepartureAuditEvent(
                plan.CompanyId,
                plan.Id,
                plan.EmployeeId,
                tasks.Count(t => t.IsMandatory && t.Status != OffboardingTaskStatus.Completed),
                now),
            cancellationToken);

        var hrAdministratorIds = await hrAdministratorDirectory.GetHrAdministratorEmployeeIdsAsync(
            plan.CompanyId, cancellationToken);

        foreach (var hrAdministratorId in hrAdministratorIds)
        {
            await notificationWriter.WriteAsync(
                Guid.NewGuid(), plan.CompanyId, hrAdministratorId,
                "Employee departed with incomplete offboarding",
                "This employee's departure was finalised while mandatory offboarding tasks were " +
                    "still outstanding. This exception remains open until offboarding is completed.",
                plan.Id,
                NotificationType.IncompleteOffboardingAtDeparture,
                NotificationPriority.High,
                now,
                cancellationToken);
        }
    }
}
