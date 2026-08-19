using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Features.GetTeamOnboarding;

internal sealed class GetTeamOnboardingHandler(
    OnboardingDbContext dbContext,
    IDirectReportsReader directReportsReader,
    IEmployeeNameReader employeeNameReader)
{
    public async Task<GetTeamOnboardingResponse> HandleAsync(
        GetTeamOnboardingRequest request,
        CancellationToken cancellationToken)
    {
        var directReportIds = await directReportsReader.GetDirectReportIdsAsync(
            request.CompanyId,
            request.ManagerId,
            cancellationToken);

        if (directReportIds.Count == 0)
            return new GetTeamOnboardingResponse([]);

        var plans = await dbContext.OnboardingPlans
            .AsNoTracking()
            .Where(p => p.CompanyId == request.CompanyId
                     && directReportIds.Contains(p.EmployeeId)
                     && (p.Status == OnboardingStatus.NotStarted || p.Status == OnboardingStatus.InProgress))
            .OrderBy(p => p.StartDate)
            .ToListAsync(cancellationToken);

        if (plans.Count == 0)
            return new GetTeamOnboardingResponse([]);

        var planIds = plans.Select(p => p.Id).ToList();

        var tasks = await dbContext.OnboardingTasks
            .AsNoTracking()
            .Where(t => planIds.Contains(t.OnboardingPlanId))
            .ToListAsync(cancellationToken);

        var tasksByPlan = tasks.ToLookup(t => t.OnboardingPlanId);

        var nameMap = await employeeNameReader.GetNamesAsync(
            request.CompanyId,
            plans.Select(p => p.EmployeeId),
            cancellationToken);

        var items = plans.Select(p =>
        {
            var planTasks = tasksByPlan[p.Id];
            var totalTasks = planTasks.Count();
            var completedTasks = planTasks.Count(t =>
                t.Status == OnboardingTaskStatus.Completed || t.Status == OnboardingTaskStatus.Skipped);
            var percentComplete = totalTasks == 0 ? 0 : (int)Math.Round(completedTasks * 100.0 / totalTasks);

            return new TeamOnboardingItem(
                p.EmployeeId,
                nameMap.GetValueOrDefault(p.EmployeeId, "Employee"),
                p.Status.ToString(),
                p.StartDate,
                totalTasks,
                completedTasks,
                percentComplete);
        }).ToList();

        return new GetTeamOnboardingResponse(items);
    }
}
