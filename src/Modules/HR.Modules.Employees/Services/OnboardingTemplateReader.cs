using HR.Modules.Employees.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Services;

internal sealed class OnboardingTemplateReader(
    EmployeesDbContext dbContext,
    OnboardingTemplateSeeder templateSeeder,
    IClock clock) : IOnboardingTemplateReader
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
        var templateId = await dbContext.PositionProfiles
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.Id == positionProfileId)
            .Select(p => p.OnboardingTemplateId)
            .SingleOrDefaultAsync(cancellationToken);

        if (templateId is null)
            return null;

        var isActive = await dbContext.OnboardingTemplates
            .AsNoTracking()
            .AnyAsync(t => t.CompanyId == companyId && t.Id == templateId && t.IsActive, cancellationToken);

        return isActive ? templateId : null;
    }

    public async Task<Guid?> GetDefaultOnboardingTemplateIdAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        await templateSeeder.EnsureDefaultTemplateSeededAsync(companyId, clock.UtcNowOffset(), cancellationToken);

        return await dbContext.OnboardingTemplates
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.IsActive && t.IsDefault)
            .Select(t => (Guid?)t.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
