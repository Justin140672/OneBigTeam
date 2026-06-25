using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreatePositionProfile;

internal sealed class CreatePositionProfileHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;

    public CreatePositionProfileHandler(EmployeesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<CreatePositionProfileResponse>> HandleAsync(
        CreatePositionProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (request.DepartmentId is not null)
        {
            var departmentExists = await _dbContext.Departments
                .AnyAsync(
                    d => d.Id == request.DepartmentId &&
                         d.CompanyId == request.CompanyId &&
                         d.IsActive,
                    cancellationToken);

            if (!departmentExists)
            {
                return Result.Failure<CreatePositionProfileResponse>(
                    Error.NotFound($"Department '{request.DepartmentId}' was not found."));
            }
        }

        var titleExists = await _dbContext.PositionProfiles
            .AnyAsync(
                p => p.CompanyId == request.CompanyId &&
                     p.Title == request.Title.Trim() &&
                     p.IsActive,
                cancellationToken);

        if (titleExists)
        {
            return Result.Failure<CreatePositionProfileResponse>(
                Error.Conflict($"An active position profile titled '{request.Title.Trim()}' already exists in this company."));
        }

        var now = _clock.UtcNowOffset();

        var profile = PositionProfile.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.DepartmentId,
            request.Title.Trim(),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            request.IsManagerial,
            request.ProbationMonthsOverride,
            now);

        _dbContext.PositionProfiles.Add(profile);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreatePositionProfileResponse(
            profile.Id,
            profile.CompanyId,
            profile.DepartmentId,
            profile.Title,
            profile.Description,
            profile.IsManagerial,
            profile.ProbationMonthsOverride,
            profile.IsActive,
            profile.CreatedAt));
    }
}
