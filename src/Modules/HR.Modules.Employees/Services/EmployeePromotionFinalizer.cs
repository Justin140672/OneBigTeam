using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;

namespace HR.Modules.Employees.Services;

// Extracted from PromoteEmployeeHandler so the exact same finalisation steps (apply the new
// position/location/manager to the employee, complete the promotion, publish the completion audit
// event + integration event) run whether triggered immediately by the handler (same-day/backdated
// effective date) or later by ProcessPromotionsJob. EmployeePromotion.Complete guards its own state
// transition (throws unless still pending), which is what keeps repeated calls safe.
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
        employee.Assign(
            employee.DepartmentId,
            promotion.NewPositionProfileId,
            promotion.NewLocationId ?? employee.LocationId,
            promotion.NewManagerId ?? employee.ManagerId,
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
    }
}
