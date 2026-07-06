using HR.Modules.Employees.Persistence;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class OnboardingTemplateReader(EmployeesDbContext dbContext) : IOnboardingTemplateReader
{
    public async Task<IReadOnlyList<OnboardingTemplateTaskItem>> GetActiveTasksAsync(
        Guid companyId,
        Guid onboardingTemplateId,
        CancellationToken cancellationToken)
    {
        return await dbContext.OnboardingTemplateTasks
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId
                     && t.OnboardingTemplateId == onboardingTemplateId
                     && t.IsActive)
            .OrderBy(t => t.DisplayOrder)
            .Select(t => new OnboardingTemplateTaskItem(
                t.Id,
                t.Title,
                t.Description,
                t.Priority,
                t.AssignTo,
                t.DueDaysAfterStart,
                t.DisplayOrder))
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid?> GetOnboardingTemplateIdForPositionProfileAsync(
        Guid companyId,
        Guid positionProfileId,
        CancellationToken cancellationToken)
    {
        return await dbContext.PositionProfiles
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.Id == positionProfileId)
            .Select(p => p.OnboardingTemplateId)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
