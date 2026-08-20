using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.UpdateLocation;

internal sealed class UpdateLocationHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;

    public UpdateLocationHandler(EmployeesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<UpdateLocationResponse>> HandleAsync(
        UpdateLocationRequest request,
        CancellationToken cancellationToken)
    {
        var location = await _dbContext.Locations
            .SingleOrDefaultAsync(
                l => l.Id == request.Id && l.CompanyId == request.CompanyId && l.IsActive,
                cancellationToken);

        if (location is null)
        {
            return Result.Failure<UpdateLocationResponse>(
                Error.NotFound($"Location '{request.Id}' was not found."));
        }

        if (request.LocationTypeId != location.LocationTypeId)
        {
            var locationTypeExists = await _dbContext.LocationTypes
                .AnyAsync(
                    t => t.Id == request.LocationTypeId &&
                         t.CompanyId == request.CompanyId &&
                         t.IsActive,
                    cancellationToken);

            if (!locationTypeExists)
            {
                return Result.Failure<UpdateLocationResponse>(
                    Error.NotFound($"Location type '{request.LocationTypeId}' was not found."));
            }
        }

        var newName = request.Name.Trim();
        if (!string.Equals(location.Name, newName, StringComparison.OrdinalIgnoreCase))
        {
            var nameExists = await _dbContext.Locations
                .AnyAsync(
                    l => l.CompanyId == request.CompanyId &&
                         l.Id != request.Id &&
                         l.Name.ToLower() == newName.ToLower() &&
                         l.IsActive,
                    cancellationToken);

            if (nameExists)
            {
                return Result.Failure<UpdateLocationResponse>(
                    Error.Conflict($"An active location named '{newName}' already exists in this company."));
            }
        }

        var now = _clock.UtcNowOffset();

        location.Update(
            newName,
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            request.LocationTypeId,
            now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdateLocationResponse(
            location.Id,
            location.CompanyId,
            location.Name,
            location.Description,
            location.LocationTypeId,
            location.IsActive,
            location.UpdatedAt));
    }
}
