using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.UpdateLocationType;

internal sealed class UpdateLocationTypeHandler(EmployeesDbContext db, IClock clock)
{
    public async Task<Result<UpdateLocationTypeResponse>> HandleAsync(
        UpdateLocationTypeRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.LocationTypes
            .FirstOrDefaultAsync(e => e.Id == request.Id && e.CompanyId == request.CompanyId, cancellationToken);

        if (entity is null)
            return Result.Failure<UpdateLocationTypeResponse>(Error.NotFound("Location type not found."));

        var nameConflict = await db.LocationTypes.AnyAsync(
            e => e.CompanyId == request.CompanyId && e.Name == request.Name && e.Id != request.Id,
            cancellationToken);

        if (nameConflict)
            return Result.Failure<UpdateLocationTypeResponse>(
                Error.Conflict($"A location type named '{request.Name}' already exists."));

        entity.Update(request.Name, request.Description, new DateTimeOffset(clock.UtcNow, TimeSpan.Zero));
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdateLocationTypeResponse(
            entity.Id, entity.CompanyId, entity.Name, entity.Description, entity.IsActive, entity.UpdatedAt));
    }
}
