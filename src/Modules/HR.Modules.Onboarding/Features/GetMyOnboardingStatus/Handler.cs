using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Features.GetMyOnboardingStatus;

internal sealed class GetMyOnboardingStatusHandler(OnboardingDbContext dbContext)
{
    public async Task<GetMyOnboardingStatusResponse> HandleAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken)
    {
        var plan = await dbContext.OnboardingPlans
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.EmployeeId == employeeId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (plan is null)
            return new GetMyOnboardingStatusResponse(false, null, null, 0, 0, []);

        var tasks = await dbContext.OnboardingTasks
            .AsNoTracking()
            .Where(t => t.OnboardingPlanId == plan.Id)
            .OrderBy(t => t.DueDate)
            .Select(t => new MyOnboardingTaskItem(t.Id, t.Title, t.Status.ToString(), t.DueDate, t.CompletedAt))
            .ToListAsync(cancellationToken);

        var completed = tasks.Count(t => t.Status == OnboardingTaskStatus.Completed.ToString());

        return new GetMyOnboardingStatusResponse(
            true, plan.Status.ToString(), plan.StartDate, tasks.Count, completed, tasks);
    }
}
