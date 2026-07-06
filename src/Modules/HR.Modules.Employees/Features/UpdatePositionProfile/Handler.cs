using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.UpdatePositionProfile;

internal sealed class UpdatePositionProfileHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILeavePolicyReader _leavePolicyReader;

    public UpdatePositionProfileHandler(EmployeesDbContext dbContext, IClock clock, ILeavePolicyReader leavePolicyReader)
    {
        _dbContext = dbContext;
        _clock = clock;
        _leavePolicyReader = leavePolicyReader;
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

        if (request.DefaultLeavePolicyId is not null)
        {
            var leavePolicyExists = await _leavePolicyReader.ExistsAsync(
                request.CompanyId, request.DefaultLeavePolicyId.Value, cancellationToken);

            if (!leavePolicyExists)
            {
                return Result.Failure<UpdatePositionProfileResponse>(
                    Error.NotFound($"Leave policy '{request.DefaultLeavePolicyId}' was not found."));
            }
        }

        if (request.OnboardingTemplateId is not null)
        {
            var onboardingTemplateExists = await _dbContext.OnboardingTemplates
                .AnyAsync(
                    t => t.Id == request.OnboardingTemplateId &&
                         t.CompanyId == request.CompanyId &&
                         t.IsActive,
                    cancellationToken);

            if (!onboardingTemplateExists)
            {
                return Result.Failure<UpdatePositionProfileResponse>(
                    Error.NotFound($"Onboarding template '{request.OnboardingTemplateId}' was not found."));
            }
        }

        var now = _clock.UtcNowOffset();

        profile.Update(
            request.DepartmentId,
            newTitle,
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            request.IsManagerial,
            request.ProbationMonthsOverride,
            request.WorkingDaysOverride,
            request.HoursPerDayOverride,
            request.SalaryMin,
            request.SalaryMax,
            request.SalaryType,
            request.DefaultLeavePolicyId,
            now,
            request.OnboardingTemplateId);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new UpdatePositionProfileResponse(
            profile.Id,
            profile.CompanyId,
            profile.DepartmentId,
            profile.Title,
            profile.Description,
            profile.IsManagerial,
            profile.ProbationMonthsOverride,
            profile.WorkingDaysOverride,
            profile.HoursPerDayOverride,
            profile.SalaryMin,
            profile.SalaryMax,
            profile.SalaryType,
            profile.DefaultLeavePolicyId,
            profile.OnboardingTemplateId,
            profile.IsActive,
            profile.UpdatedAt));
    }
}
