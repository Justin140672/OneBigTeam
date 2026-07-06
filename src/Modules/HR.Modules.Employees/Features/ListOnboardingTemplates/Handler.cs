using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.ListOnboardingTemplates;

internal sealed class ListOnboardingTemplatesHandler(EmployeesDbContext dbContext)
{
    public async Task<Result<ListOnboardingTemplatesResponse>> HandleAsync(
        ListOnboardingTemplatesRequest request,
        CancellationToken cancellationToken)
    {
        var query = dbContext.OnboardingTemplates
            .AsNoTracking()
            .Include(t => t.Tasks)
            .Where(t => t.CompanyId == request.CompanyId);

        if (!request.IncludeInactive)
            query = query.Where(t => t.IsActive);

        var items = await query
            .OrderBy(t => t.Name)
            .Select(t => new OnboardingTemplateListItem(
                t.Id,
                t.Name,
                t.Description,
                t.IsActive,
                t.Tasks.Count(task => task.IsActive)))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListOnboardingTemplatesResponse(items));
    }
}
