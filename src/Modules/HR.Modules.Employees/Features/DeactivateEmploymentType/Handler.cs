using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.DeactivateEmploymentType;

internal sealed class DeactivateEmploymentTypeHandler(EmployeesDbContext db, IClock clock)
{
    public async Task<Result> HandleAsync(
        DeactivateEmploymentTypeRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.EmploymentTypes
            .FirstOrDefaultAsync(e => e.Id == request.Id && e.CompanyId == request.CompanyId, cancellationToken);

        if (entity is null)
            return Result.Failure(Error.NotFound("Employment type not found."));

        if (!entity.IsActive)
            return Result.Failure(Error.Conflict("Employment type is already inactive."));

        var currentEmployeeCount = await db.Employees
            .CountAsync(
                e => e.EmploymentTypeId == request.Id
                  && e.CompanyId == request.CompanyId
                  && e.Status != EmploymentStatus.Terminated,
                cancellationToken);

        if (currentEmployeeCount > 0)
        {
            return Result.Failure(Error.Conflict(
                $"Cannot deactivate '{entity.Name}' — it is currently assigned to " +
                $"{currentEmployeeCount} active employee{(currentEmployeeCount == 1 ? "" : "s")}."));
        }

        entity.Deactivate(new DateTimeOffset(clock.UtcNow, TimeSpan.Zero));
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
