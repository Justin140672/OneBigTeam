using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.UpdateEmploymentType;

internal sealed class UpdateEmploymentTypeHandler(EmployeesDbContext db, IClock clock)
{
    public async Task<Result<UpdateEmploymentTypeResponse>> HandleAsync(
        UpdateEmploymentTypeRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.EmploymentTypes
            .FirstOrDefaultAsync(e => e.Id == request.Id && e.CompanyId == request.CompanyId, cancellationToken);

        if (entity is null)
            return Result.Failure<UpdateEmploymentTypeResponse>(Error.NotFound("Employment type not found."));

        var nameConflict = await db.EmploymentTypes.AnyAsync(
            e => e.CompanyId == request.CompanyId && e.Name.ToLower() == request.Name.Trim().ToLower() && e.Id != request.Id,
            cancellationToken);

        if (nameConflict)
            return Result.Failure<UpdateEmploymentTypeResponse>(
                Error.Conflict($"An employment type named '{request.Name}' already exists."));

        entity.Update(request.Name, request.Description, new DateTimeOffset(clock.UtcNow, TimeSpan.Zero));

        // Ticket 2: optimistic concurrency (base-code helper). Nothing commits on conflict.
        var saveResult = await db.SaveChangesWithConcurrencyAsync(
            entity,
            request.ExpectedVersion,
            "This employment type was changed by someone else since you opened it. Reload the latest details and try again.",
            cancellationToken);

        if (saveResult.IsFailure)
            return Result.Failure<UpdateEmploymentTypeResponse>(saveResult.Error);

        return Result.Success(new UpdateEmploymentTypeResponse(
            entity.Id, entity.CompanyId, entity.Name, entity.Description, entity.IsActive, entity.UpdatedAt, entity.Version));
    }
}
