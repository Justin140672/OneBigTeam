using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.AddOnboardingTemplateToPositionProfile;

internal sealed class AddOnboardingTemplateHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<AddOnboardingTemplateResponse>> HandleAsync(
        AddOnboardingTemplateRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var profileExists = await dbContext.PositionProfiles
            .AnyAsync(
                p => p.Id == request.PositionProfileId && p.CompanyId == request.CompanyId,
                cancellationToken);

        if (!profileExists)
            return Result.Failure<AddOnboardingTemplateResponse>(
                Error.NotFound($"Position profile '{request.PositionProfileId}' was not found."));

        var templateExists = await dbContext.OnboardingTemplates
            .AnyAsync(
                t => t.Id == request.OnboardingTemplateId &&
                     t.CompanyId == request.CompanyId &&
                     t.IsActive,
                cancellationToken);

        if (!templateExists)
            return Result.Failure<AddOnboardingTemplateResponse>(
                Error.NotFound($"Onboarding template '{request.OnboardingTemplateId}' was not found."));

        var duplicateExists = await dbContext.PositionProfileOnboardingTemplates
            .AnyAsync(
                a => a.PositionProfileId == request.PositionProfileId &&
                     a.OnboardingTemplateId == request.OnboardingTemplateId &&
                     a.IsActive,
                cancellationToken);

        if (duplicateExists)
            return Result.Failure<AddOnboardingTemplateResponse>(
                Error.Conflict("This onboarding template is already assigned to the position profile."));

        var now = clock.UtcNowOffset();

        var assignment = PositionProfileOnboardingTemplate.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.PositionProfileId,
            request.OnboardingTemplateId,
            actorEmployeeId,
            now);

        dbContext.PositionProfileOnboardingTemplates.Add(assignment);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new OnboardingTemplateAssignedAuditEvent(
                request.CompanyId,
                request.PositionProfileId,
                assignment.Id,
                request.OnboardingTemplateId,
                actorEmployeeId,
                now),
            cancellationToken);

        return Result.Success(new AddOnboardingTemplateResponse(
            assignment.Id,
            assignment.PositionProfileId,
            assignment.OnboardingTemplateId));
    }
}
