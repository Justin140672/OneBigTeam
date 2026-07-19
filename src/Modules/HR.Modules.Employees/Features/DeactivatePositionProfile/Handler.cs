using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.DeactivatePositionProfile;

internal sealed class DeactivatePositionProfileHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;

    public DeactivatePositionProfileHandler(EmployeesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result> HandleAsync(
        DeactivatePositionProfileRequest request,
        CancellationToken cancellationToken)
    {
        var positionProfile = await _dbContext.PositionProfiles
            .SingleOrDefaultAsync(
                p => p.Id == request.Id && p.CompanyId == request.CompanyId && p.IsActive,
                cancellationToken);

        if (positionProfile is null)
            return Result.Failure(Error.NotFound($"Position profile '{request.Id}' was not found."));

        var currentEmployeeCount = await _dbContext.Employees
            .CountAsync(
                e => e.PositionProfileId == request.Id
                  && e.CompanyId == request.CompanyId
                  && e.Status != EmploymentStatus.Terminated,
                cancellationToken);

        if (currentEmployeeCount > 0)
        {
            return Result.Failure(Error.Conflict(
                $"Cannot deactivate '{positionProfile.Title}' — it is currently assigned to " +
                $"{currentEmployeeCount} active employee{(currentEmployeeCount == 1 ? "" : "s")}."));
        }

        positionProfile.Deactivate(_clock.UtcNowOffset());
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
