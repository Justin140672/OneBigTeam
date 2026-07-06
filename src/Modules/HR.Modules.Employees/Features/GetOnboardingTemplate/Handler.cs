using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.GetOnboardingTemplate;

internal sealed class GetOnboardingTemplateHandler(EmployeesDbContext dbContext)
{
    public async Task<Result<GetOnboardingTemplateResponse>> HandleAsync(
        GetOnboardingTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var template = await dbContext.OnboardingTemplates
            .AsNoTracking()
            .Include(t => t.Tasks)
            .SingleOrDefaultAsync(
                t => t.Id == request.Id && t.CompanyId == request.CompanyId,
                cancellationToken);

        if (template is null)
        {
            return Result.Failure<GetOnboardingTemplateResponse>(
                Error.NotFound($"Onboarding template with id '{request.Id}' was not found."));
        }

        var tasks = template.Tasks
            .Where(t => t.IsActive)
            .OrderBy(t => t.DisplayOrder)
            .Select(t => new OnboardingTemplateTaskListItem(
                t.Id,
                t.Title,
                t.Description,
                t.Priority,
                t.AssignTo,
                t.DueDaysAfterStart,
                t.DisplayOrder))
            .ToList();

        return Result.Success(new GetOnboardingTemplateResponse(
            template.Id,
            template.CompanyId,
            template.Name,
            template.Description,
            template.IsActive,
            template.CreatedAt,
            template.UpdatedAt,
            tasks));
    }
}
