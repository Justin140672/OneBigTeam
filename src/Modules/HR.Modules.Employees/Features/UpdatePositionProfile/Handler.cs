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
    private readonly IAuditEventPublisher _auditEventPublisher;

    public UpdatePositionProfileHandler(
        EmployeesDbContext dbContext,
        IClock clock,
        ILeavePolicyReader leavePolicyReader,
        IAuditEventPublisher auditEventPublisher)
    {
        _dbContext = dbContext;
        _clock = clock;
        _leavePolicyReader = leavePolicyReader;
        _auditEventPublisher = auditEventPublisher;
    }

    public async Task<Result<UpdatePositionProfileResponse>> HandleAsync(
        UpdatePositionProfileRequest request,
        Guid actorEmployeeId,
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

        var locationExists = await _dbContext.Locations
            .AnyAsync(
                l => l.Id == request.LocationId &&
                     l.CompanyId == request.CompanyId &&
                     l.IsActive,
                cancellationToken);

        if (!locationExists)
        {
            return Result.Failure<UpdatePositionProfileResponse>(
                Error.NotFound($"Location '{request.LocationId}' was not found."));
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

        var leavePolicyExists = await _leavePolicyReader.ExistsAsync(
            request.CompanyId, request.DefaultLeavePolicyId, cancellationToken);

        if (!leavePolicyExists)
        {
            return Result.Failure<UpdatePositionProfileResponse>(
                Error.NotFound($"Leave policy '{request.DefaultLeavePolicyId}' was not found."));
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

        var before = new PositionProfileSnapshot(
            profile.DepartmentId,
            profile.LocationId,
            profile.Title,
            profile.Description,
            profile.ProbationMonthsOverride,
            profile.WorkingDaysOverride,
            profile.HoursPerDayOverride,
            profile.NoticePeriodUnitOverride,
            profile.NoticePeriodLengthOverride,
            profile.SalaryMin,
            profile.SalaryMax,
            profile.SalaryType,
            profile.DefaultLeavePolicyId,
            profile.OnboardingTemplateId,
            profile.IsActive);

        profile.Update(
            request.DepartmentId,
            request.LocationId,
            newTitle,
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            request.ProbationMonthsOverride,
            request.WorkingDaysOverride,
            request.HoursPerDayOverride,
            request.SalaryMin,
            request.SalaryMax,
            request.SalaryType,
            request.DefaultLeavePolicyId,
            now,
            request.OnboardingTemplateId,
            request.NoticePeriodUnitOverride,
            request.NoticePeriodLengthOverride);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var after = new PositionProfileSnapshot(
            profile.DepartmentId,
            profile.LocationId,
            profile.Title,
            profile.Description,
            profile.ProbationMonthsOverride,
            profile.WorkingDaysOverride,
            profile.HoursPerDayOverride,
            profile.NoticePeriodUnitOverride,
            profile.NoticePeriodLengthOverride,
            profile.SalaryMin,
            profile.SalaryMax,
            profile.SalaryType,
            profile.DefaultLeavePolicyId,
            profile.OnboardingTemplateId,
            profile.IsActive);

        await _auditEventPublisher.PublishAsync(
            new PositionProfileUpdatedAuditEvent(profile.CompanyId, profile.Id, actorEmployeeId, now, before, after),
            cancellationToken);

        return Result.Success(new UpdatePositionProfileResponse(
            profile.Id,
            profile.CompanyId,
            profile.DepartmentId,
            profile.LocationId,
            profile.Title,
            profile.Description,
            profile.ProbationMonthsOverride,
            profile.WorkingDaysOverride,
            profile.HoursPerDayOverride,
            profile.NoticePeriodUnitOverride,
            profile.NoticePeriodLengthOverride,
            profile.SalaryMin,
            profile.SalaryMax,
            profile.SalaryType,
            profile.DefaultLeavePolicyId,
            profile.OnboardingTemplateId,
            profile.IsActive,
            profile.UpdatedAt));
    }
}
