using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.UpdatePositionProfile;

internal sealed class UpdatePositionProfileHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;

    public UpdatePositionProfileHandler(EmployeesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<UpdatePositionProfileResponse>> HandleAsync(
        UpdatePositionProfileRequest request,
        CancellationToken cancellationToken)
    {
        var profile = await _dbContext.PositionProfiles
            .SingleOrDefaultAsync(
                p => p.Id == request.Id && p.CompanyId == request.CompanyId,
                cancellationToken);

        if (profile is null)
        {
            return Result.Failure<UpdatePositionProfileResponse>(
                Error.NotFound($"Position profile with id '{request.Id}' was not found."));
        }

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
                return Result.Failure<UpdatePositionProfileResponse>(
                    Error.NotFound($"Department '{request.DepartmentId}' was not found."));
            }
        }

        var newTitle = request.Title.Trim();

        if (!string.Equals(profile.Title, newTitle, StringComparison.Ordinal))
        {
            var titleExists = await _dbContext.PositionProfiles
                .AnyAsync(
                    p => p.CompanyId == request.CompanyId &&
                         p.Id != request.Id &&
                         p.Title == newTitle &&
                         p.IsActive,
                    cancellationToken);

            if (titleExists)
            {
                return Result.Failure<UpdatePositionProfileResponse>(
                    Error.Conflict($"An active position profile titled '{newTitle}' already exists in this company."));
            }
        }

        var now = _clock.UtcNowOffset();

        profile.Update(
            request.DepartmentId,
            newTitle,
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            request.IsManagerial,
            now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdatePositionProfileResponse(
            profile.Id,
            profile.CompanyId,
            profile.DepartmentId,
            profile.Title,
            profile.Description,
            profile.IsManagerial,
            profile.IsActive,
            profile.UpdatedAt));
    }
}
