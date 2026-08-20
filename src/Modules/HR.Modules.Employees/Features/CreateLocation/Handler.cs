using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreateLocation;

internal sealed class CreateLocationHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;

    public CreateLocationHandler(EmployeesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<CreateLocationResponse>> HandleAsync(
        CreateLocationRequest request,
        CancellationToken cancellationToken)
    {
        var locationTypeExists = await _dbContext.LocationTypes
            .AnyAsync(
                t => t.Id == request.LocationTypeId &&
                     t.CompanyId == request.CompanyId &&
                     t.IsActive,
                cancellationToken);

        if (!locationTypeExists)
        {
            return Result.Failure<CreateLocationResponse>(
                Error.NotFound($"Location type '{request.LocationTypeId}' was not found."));
        }

        var nameExists = await _dbContext.Locations
            .AnyAsync(
                l => l.CompanyId == request.CompanyId &&
                     l.Name.ToLower() == request.Name.Trim().ToLower() &&
                     l.IsActive,
                cancellationToken);

        if (nameExists)
        {
            return Result.Failure<CreateLocationResponse>(
                Error.Conflict($"An active location named '{request.Name.Trim()}' already exists in this company."));
        }

        var now = _clock.UtcNowOffset();

        var location = Location.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.LocationTypeId,
            request.Name.Trim(),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            now);

        _dbContext.Locations.Add(location);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateLocationResponse(
            location.Id,
            location.CompanyId,
            location.Name,
            location.Description,
            location.LocationTypeId,
            location.IsActive,
            location.CreatedAt));
    }
}
