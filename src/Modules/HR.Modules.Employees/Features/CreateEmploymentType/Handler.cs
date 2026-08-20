using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreateEmploymentType;

internal sealed class CreateEmploymentTypeHandler(EmployeesDbContext db, IClock clock)
{
    public async Task<Result<CreateEmploymentTypeResponse>> HandleAsync(
        CreateEmploymentTypeRequest request,
        CancellationToken cancellationToken)
    {
        var exists = await db.EmploymentTypes.AnyAsync(
            e => e.CompanyId == request.CompanyId && e.Name.ToLower() == request.Name.Trim().ToLower(),
            cancellationToken);

        if (exists)
            return Result.Failure<CreateEmploymentTypeResponse>(
                Error.Conflict($"An employment type named '{request.Name}' already exists."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        var entity = EmploymentType.Create(Guid.NewGuid(), request.CompanyId, request.Name, request.Description, now);

        db.EmploymentTypes.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateEmploymentTypeResponse(
            entity.Id, entity.CompanyId, entity.Name, entity.Description,
            entity.IsActive, entity.CreatedAt, entity.UpdatedAt));
    }
}
