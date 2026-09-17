using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Tasks.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Features.GetOffboardingOverview;

internal sealed class GetOffboardingOverviewHandler(
    OffboardingDbContext dbContext,
    IOpenTaskBySourceEntityReader openTaskReader)
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
                false,
                false,
                false,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                false,
                []);
        }

        var tasks = await dbContext.OffboardingTasks
            .AsNoTracking()
            .Where(t => t.OffboardingPlanId == plan.Id)
            .ToListAsync(cancellationToken);

        var progress = OffboardingProgressCalculator.Calculate(tasks);

        var openTaskIds = await openTaskReader.GetOpenTaskIdsAsync(
            request.CompanyId, tasks.Select(t => t.Id), cancellationToken);

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
                t.UpdatedAt,
                t.RequiresHrConfirmation,
                t.IsMandatory,
                t.SkipReason,
                t.SkippedByUserId,
                t.SkippedAt,
                openTaskIds.TryGetValue(t.Id, out var openTaskId) ? openTaskId : (Guid?)null,
                t.AssetAssignmentId))
            .ToList();

        return new GetOffboardingOverviewResponse(
            request.EmployeeId,
            true,
            plan.Status.ToString(),
            plan.LastWorkingDay,
            plan.Notes,
            plan.IsBackdated,
            plan.RequiresHrReconciliation,
            plan.HasIncompleteOffboardingAtDeparture,
            progress.TotalTasks,
            progress.ResolvedTasks,
            progress.ProgressPercent,
            progress.RequiredTotal,
            progress.RequiredResolved,
            progress.TotalCount,
            progress.TotalResolved,
            plan.Status == OffboardingStatus.InProgress && plan.HasIncompleteOffboardingAtDeparture,
            taskItems);
    }
}
