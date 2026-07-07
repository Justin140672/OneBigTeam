using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.DeactivateLocationType;

internal sealed class DeactivateLocationTypeHandler(EmployeesDbContext db, IClock clock)
{
    public async Task<Result> HandleAsync(
        DeactivateLocationTypeRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.LocationTypes
            .FirstOrDefaultAsync(e => e.Id == request.Id && e.CompanyId == request.CompanyId, cancellationToken);

        if (entity is null)
            return Result.Failure(Error.NotFound("Location type not found."));

        if (!entity.IsActive)
            return Result.Failure(Error.Conflict("Location type is already inactive."));

        entity.Deactivate(new DateTimeOffset(clock.UtcNow, TimeSpan.Zero));
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
