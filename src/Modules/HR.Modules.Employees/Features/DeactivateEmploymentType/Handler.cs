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

        entity.Deactivate(new DateTimeOffset(clock.UtcNow, TimeSpan.Zero));
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
