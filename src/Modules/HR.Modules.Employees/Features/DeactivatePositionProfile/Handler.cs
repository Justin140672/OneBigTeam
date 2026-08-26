using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.DeactivatePositionProfile;

internal sealed class DeactivatePositionProfileHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IIntegrationEventPublisher _integrationEventPublisher;

    public DeactivatePositionProfileHandler(
        EmployeesDbContext dbContext, IClock clock, IIntegrationEventPublisher integrationEventPublisher)
    {
        _dbContext = dbContext;
        _clock = clock;
        _integrationEventPublisher = integrationEventPublisher;
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
                  && e.Status != EmploymentStatus.FormerEmployee,
                cancellationToken);

        if (currentEmployeeCount > 0)
        {
            return Result.Failure(Error.Conflict(
                $"Cannot deactivate '{positionProfile.Title}' — it is currently assigned to " +
                $"{currentEmployeeCount} active employee{(currentEmployeeCount == 1 ? "" : "s")}."));
        }

        var now = _clock.UtcNowOffset();
        positionProfile.Deactivate(now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _integrationEventPublisher.PublishAsync(
            new PositionProfileUpsertedIntegrationEvent(
                positionProfile.CompanyId, positionProfile.Id, positionProfile.Title, positionProfile.IsActive, now),
            cancellationToken);

        return Result.Success();
    }
}
