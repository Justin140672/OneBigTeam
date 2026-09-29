using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Services;

internal interface IEmployeePromotionFinalizer
{
    Task FinalizeAsync(
        Employee employee,
        EmployeePromotion promotion,
        Guid? actorEmployeeId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
