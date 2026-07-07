using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreateLocationType;

internal sealed class CreateLocationTypeHandler(EmployeesDbContext db, IClock clock)
{
    public async Task<Result<CreateLocationTypeResponse>> HandleAsync(
        CreateLocationTypeRequest request,
        CancellationToken cancellationToken)
    {
        var exists = await db.LocationTypes.AnyAsync(
            e => e.CompanyId == request.CompanyId && e.Name == request.Name,
            cancellationToken);

        if (exists)
            return Result.Failure<CreateLocationTypeResponse>(
                Error.Conflict($"A location type named '{request.Name}' already exists."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        var entity = LocationType.Create(Guid.NewGuid(), request.CompanyId, request.Name, request.Description, now);

        db.LocationTypes.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateLocationTypeResponse(
            entity.Id, entity.CompanyId, entity.Name, entity.Description,
            entity.IsActive, entity.CreatedAt, entity.UpdatedAt));
    }
}
