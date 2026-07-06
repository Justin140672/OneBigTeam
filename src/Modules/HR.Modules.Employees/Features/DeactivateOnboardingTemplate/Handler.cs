using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.DeactivateOnboardingTemplate;

internal sealed class DeactivateOnboardingTemplateHandler(EmployeesDbContext dbContext, IClock clock)
{
    public async Task<Result> HandleAsync(
        DeactivateOnboardingTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var template = await dbContext.OnboardingTemplates
            .SingleOrDefaultAsync(
                t => t.Id == request.Id &&
                     t.CompanyId == request.CompanyId &&
                     t.IsActive,
                cancellationToken);

        if (template is null)
            return Result.Failure(Error.NotFound($"Onboarding template '{request.Id}' was not found."));

        template.Deactivate(clock.UtcNowOffset());
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
