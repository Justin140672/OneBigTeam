using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.DeactivateLocation;

internal sealed class DeactivateLocationHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;

    public DeactivateLocationHandler(EmployeesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result> HandleAsync(
        DeactivateLocationRequest request,
        CancellationToken cancellationToken)
    {
        var location = await _dbContext.Locations
            .SingleOrDefaultAsync(
                l => l.Id == request.Id && l.CompanyId == request.CompanyId && l.IsActive,
                cancellationToken);

        if (location is null)
            return Result.Failure(Error.NotFound($"Location '{request.Id}' was not found."));

        var currentEmployeeCount = await _dbContext.Employees
            .CountAsync(
                e => e.LocationId == request.Id
                  && e.CompanyId == request.CompanyId
                  && e.Status != EmploymentStatus.FormerEmployee,
                cancellationToken);

        if (currentEmployeeCount > 0)
        {
            return Result.Failure(Error.Conflict(
                $"Cannot deactivate '{location.Name}' — it is currently assigned to " +
                $"{currentEmployeeCount} active employee{(currentEmployeeCount == 1 ? "" : "s")}."));
        }

        location.Deactivate(_clock.UtcNowOffset());
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
