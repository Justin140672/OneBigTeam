using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.RemoveOnboardingTemplateFromPositionProfile;

internal sealed class RemoveOnboardingTemplateHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result> HandleAsync(
        RemoveOnboardingTemplateRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var assignment = await dbContext.PositionProfileOnboardingTemplates
            .SingleOrDefaultAsync(
                a => a.Id == request.Id &&
                     a.PositionProfileId == request.PositionProfileId &&
                     a.CompanyId == request.CompanyId &&
                     a.IsActive,
                cancellationToken);

        if (assignment is null)
            return Result.Failure(
                Error.NotFound($"Onboarding template assignment '{request.Id}' was not found on this position profile."));

        assignment.Deactivate();
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new OnboardingTemplateRemovedAuditEvent(
                request.CompanyId,
                request.PositionProfileId,
                assignment.Id,
                assignment.OnboardingTemplateId,
                actorEmployeeId,
                clock.UtcNowOffset()),
            cancellationToken);

        return Result.Success();
    }
}
