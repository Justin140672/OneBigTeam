using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreatePositionProfile;

internal sealed class CreatePositionProfileHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILeavePolicyReader _leavePolicyReader;
    private readonly IAuditEventPublisher _auditEventPublisher;

    public CreatePositionProfileHandler(
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

    public async Task<Result<CreatePositionProfileResponse>> HandleAsync(
        CreatePositionProfileRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
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

        var locationExists = await _dbContext.Locations
            .AnyAsync(
                l => l.Id == request.LocationId &&
                     l.CompanyId == request.CompanyId &&
                     l.IsActive,
                cancellationToken);

        if (!locationExists)
        {
            return Result.Failure<CreatePositionProfileResponse>(
                Error.NotFound($"Location '{request.LocationId}' was not found."));
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

        var leavePolicyExists = await _leavePolicyReader.ExistsAsync(
            request.CompanyId, request.DefaultLeavePolicyId, cancellationToken);

        if (!leavePolicyExists)
        {
            return Result.Failure<CreatePositionProfileResponse>(
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
                return Result.Failure<CreatePositionProfileResponse>(
                    Error.NotFound($"Onboarding template '{request.OnboardingTemplateId}' was not found."));
            }
        }

        var now = _clock.UtcNowOffset();

        var profile = PositionProfile.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.DepartmentId,
            request.LocationId,
            request.Title.Trim(),
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

        _dbContext.PositionProfiles.Add(profile);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditEventPublisher.PublishAsync(
            new PositionProfileCreatedAuditEvent(
                profile.CompanyId,
                profile.Id,
                actorEmployeeId,
                now,
                new PositionProfileSnapshot(
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
                    profile.IsActive)),
            cancellationToken);

        return Result.Success(new CreatePositionProfileResponse(
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
            profile.CreatedAt));
    }
}
