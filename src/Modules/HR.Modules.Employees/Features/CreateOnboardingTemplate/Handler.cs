using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreateOnboardingTemplate;

internal sealed class CreateOnboardingTemplateHandler(EmployeesDbContext dbContext, IClock clock)
{
    public async Task<Result<CreateOnboardingTemplateResponse>> HandleAsync(
        CreateOnboardingTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var newName = request.Name.Trim();

        var nameExists = await dbContext.OnboardingTemplates
            .AnyAsync(
                t => t.CompanyId == request.CompanyId &&
                     t.Name == newName &&
                     t.IsActive,
                cancellationToken);

        if (nameExists)
        {
            return Result.Failure<CreateOnboardingTemplateResponse>(
                Error.Conflict($"An active onboarding template named '{newName}' already exists in this company."));
        }

        var now = clock.UtcNowOffset();

        var template = OnboardingTemplate.Create(
            Guid.NewGuid(),
            request.CompanyId,
            newName,
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            now);

        dbContext.OnboardingTemplates.Add(template);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateOnboardingTemplateResponse(
            template.Id,
            template.CompanyId,
            template.Name,
            template.Description,
            template.IsActive,
            template.CreatedAt));
    }
}
