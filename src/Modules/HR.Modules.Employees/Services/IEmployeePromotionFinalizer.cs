using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Services;

// The single idempotent finalisation operation for promotions, mirroring
// IEmployeeDepartureFinalizer. PromoteEmployeeHandler (when the effective date is today or
// backdated) and ProcessPromotionsJob (once the effective date becomes due) both call this same
// path so a promotion is always applied the same way regardless of trigger.
internal interface IEmployeePromotionFinalizer
{
    Task FinalizeAsync(
        Employee employee,
        EmployeePromotion promotion,
        Guid? actorEmployeeId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
