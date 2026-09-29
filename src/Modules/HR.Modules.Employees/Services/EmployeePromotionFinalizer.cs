using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Employees.Services;

internal sealed class EmployeePromotionFinalizer(
    EmployeesDbContext dbContext,
    IAuditEventPublisher auditEventPublisher,
    IIntegrationEventPublisher integrationEventPublisher) : IEmployeePromotionFinalizer
{
    public async Task FinalizeAsync(
        Employee employee,
        EmployeePromotion promotion,
        Guid? actorEmployeeId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var previousManagerId = employee.ManagerId;
        var previousLocationId = employee.LocationId;

        // Internal recruitment Ticket 7: the department now moves with the new position profile
        // (NewDepartmentId is captured from that profile when the promotion is recorded), and a
        // promotion may explicitly leave the employee with no manager (ClearsManager). Promotions
        // recorded before these columns existed keep the old behaviour (department unchanged,
        // null NewManagerId = keep the current manager).
        employee.Assign(
            promotion.NewDepartmentId ?? employee.DepartmentId,
            promotion.NewPositionProfileId,
            promotion.NewLocationId ?? employee.LocationId,
            promotion.ResolveManagerId(employee.ManagerId),
            now);

        promotion.Complete(now);

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new EmployeePromotionCompletedAuditEvent(
                promotion.CompanyId,
                promotion.EmployeeId,
                promotion.Id,
                now,
                promotion.PreviousPositionProfileId,
                promotion.NewPositionProfileId,
                promotion.EffectiveDate),
            cancellationToken);

        await integrationEventPublisher.PublishAsync(
            new EmployeePromotedIntegrationEvent(
                promotion.CompanyId,
                promotion.EmployeeId,
                promotion.PreviousPositionProfileId,
                promotion.NewPositionProfileId,
                promotion.EffectiveDate,
                promotion.Id),
            cancellationToken);

        // Internal recruitment Ticket 7: a promotion that changes the manager or location now
        // announces it the same way AssignManager/UpdateEmploymentDetails do, so the timeline records
        // the change and consumers such as Probation's review-task reassignment stay correct.
        // Neither event has a notification consumer, so the previous manager is never notified.
        if (previousManagerId != employee.ManagerId)
        {
            await integrationEventPublisher.PublishAsync(
                new EmployeeManagerChangedIntegrationEvent(
                    employee.CompanyId, employee.Id, previousManagerId, employee.ManagerId, now),
                cancellationToken);
        }

        if (previousLocationId != employee.LocationId)
        {
            await integrationEventPublisher.PublishAsync(
                new EmployeeLocationChangedIntegrationEvent(
                    employee.CompanyId, employee.Id, previousLocationId, employee.LocationId, now),
                cancellationToken);
        }
    }
}
