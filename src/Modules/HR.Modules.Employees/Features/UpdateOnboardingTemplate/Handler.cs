using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.UpdateOnboardingTemplate;

internal sealed class UpdateOnboardingTemplateHandler(EmployeesDbContext dbContext, IClock clock)
{
    public async Task<Result<UpdateOnboardingTemplateResponse>> HandleAsync(
        UpdateOnboardingTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var template = await dbContext.OnboardingTemplates
            .Include(t => t.Tasks)
            .SingleOrDefaultAsync(
                t => t.Id == request.Id && t.CompanyId == request.CompanyId,
                cancellationToken);

        if (template is null)
        {
            return Result.Failure<UpdateOnboardingTemplateResponse>(
                Error.NotFound($"Onboarding template with id '{request.Id}' was not found."));
        }

        var newName = request.Name.Trim();

        if (!string.Equals(template.Name, newName, StringComparison.Ordinal))
        {
            var nameExists = await dbContext.OnboardingTemplates
                .AnyAsync(
                    t => t.CompanyId == request.CompanyId &&
                         t.Id != request.Id &&
                         t.Name == newName &&
                         t.IsActive,
                    cancellationToken);

            if (nameExists)
            {
                return Result.Failure<UpdateOnboardingTemplateResponse>(
                    Error.Conflict($"An active onboarding template named '{newName}' already exists in this company."));
            }
        }

        var now = clock.UtcNowOffset();

        template.Update(
            newName,
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            now);

        var desiredTasks = request.Tasks
            .Select(t => (
                t.Id,
                Title: t.Title.Trim(),
                Description: string.IsNullOrWhiteSpace(t.Description) ? null : t.Description.Trim(),
                t.Priority,
                t.AssignTo,
                t.DueDaysAfterStart,
                t.DisplayOrder))
            .ToList();

        template.ReplaceTasks(desiredTasks, now);

        await dbContext.SaveChangesAsync(cancellationToken);

        var tasks = template.Tasks
            .Where(t => t.IsActive)
            .OrderBy(t => t.DisplayOrder)
            .Select(t => new UpdateOnboardingTemplateTaskResult(
                t.Id,
                t.Title,
                t.Description,
                t.Priority,
                t.AssignTo,
                t.DueDaysAfterStart,
                t.DisplayOrder))
            .ToList();

        return Result.Success(new UpdateOnboardingTemplateResponse(
            template.Id,
            template.CompanyId,
            template.Name,
            template.Description,
            template.IsActive,
            template.UpdatedAt,
            tasks));
    }
}
