using HR.Modules.Onboarding.Persistence;
using HR.Modules.Tasks.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Services;

internal sealed class OnboardingEmployeeTaskSourceProvider(OnboardingDbContext dbContext) : IEmployeeRelatedTaskSourceProvider
{
    public async Task<IReadOnlyCollection<Guid>> GetSourceEntityIdsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        return await dbContext.OnboardingTasks
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId
                     && dbContext.OnboardingPlans.Any(p => p.Id == t.OnboardingPlanId && p.EmployeeId == employeeId))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
    }
}
