using HR.Modules.Offboarding.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Features.GetOffboardingOverview;

internal sealed class GetOffboardingOverviewHandler(OffboardingDbContext dbContext)
{
    public async Task<GetOffboardingOverviewResponse> HandleAsync(
        GetOffboardingOverviewRequest request,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.OffboardingPlans
            .AsNoTracking()
            .Where(p => p.CompanyId == request.CompanyId && p.EmployeeId == request.EmployeeId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (plan is null)
        {
            return new GetOffboardingOverviewResponse(
                request.EmployeeId,
                false,
                null,
                null,
                null,
                []);
        }

        var tasks = await dbContext.OffboardingTasks
            .AsNoTracking()
            .Where(t => t.OffboardingPlanId == plan.Id)
            .ToListAsync(cancellationToken);

        var taskItems = tasks
            .Select(t => new OffboardingTaskOverviewItem(
                t.Id,
                t.Title,
                t.Description,
                t.AssignTo.ToString(),
                t.Status.ToString(),
                t.DueDate,
                t.CompletedAt,
                t.CreatedAt,
                t.UpdatedAt))
            .ToList();

        return new GetOffboardingOverviewResponse(
            request.EmployeeId,
            true,
            plan.Status.ToString(),
            plan.LastWorkingDay,
            plan.Notes,
            taskItems);
    }
}
