using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Offboarding.Services;

internal sealed class OffboardingManagerTaskAssigneeReconciler(
    OffboardingDbContext dbContext,
    IManagerReader managerReader,
    ITaskReassigner taskReassigner,
    IClock clock,
    ILogger<OffboardingManagerTaskAssigneeReconciler> logger)
{
    public async Task<int> ReconcileAllActivePlansAsync(CancellationToken cancellationToken)
    {
        var activePlans = await dbContext.OffboardingPlans
            .AsNoTracking()
            .Where(p => p.Status != OffboardingStatus.Completed && p.Status != OffboardingStatus.Cancelled)
            .Select(p => new { p.Id, p.CompanyId, p.EmployeeId })
            .ToListAsync(cancellationToken);

        var reassigned = 0;

        foreach (var plan in activePlans)
        {
            try
            {
                reassigned += await ReconcilePlanAsync(plan.CompanyId, plan.Id, plan.EmployeeId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Reconciling manager task assignees threw for offboarding plan {OffboardingPlanId} " +
                    "(employee {EmployeeId}, company {CompanyId}).",
                    plan.Id, plan.EmployeeId, plan.CompanyId);
            }
        }

        return reassigned;
    }

    public async Task<int> ReconcileEmployeeAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken)
    {
        var activePlanIds = await dbContext.OffboardingPlans
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId
                && p.EmployeeId == employeeId
                && p.Status != OffboardingStatus.Completed
                && p.Status != OffboardingStatus.Cancelled)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var reassigned = 0;
        foreach (var planId in activePlanIds)
            reassigned += await ReconcilePlanAsync(companyId, planId, employeeId, cancellationToken);

        return reassigned;
    }

    private async Task<int> ReconcilePlanAsync(
        Guid companyId, Guid planId, Guid employeeId, CancellationToken cancellationToken)
    {
        var managerId = await managerReader.GetManagerIdAsync(companyId, employeeId, cancellationToken);
        if (managerId is not { } resolvedManagerId)
            return 0;

        var managerTasks = await dbContext.OffboardingTasks
            .Where(t => t.CompanyId == companyId
                && t.OffboardingPlanId == planId
                && t.AssignTo == OffboardingTaskAssignTo.Manager
                && (t.Status == OffboardingTaskStatus.Pending || t.Status == OffboardingTaskStatus.InProgress))
            .ToListAsync(cancellationToken);

        if (managerTasks.Count == 0)
            return 0;

        var now = clock.UtcNowOffset();
        var changedLocally = managerTasks.Count(t => t.ReassignTo(resolvedManagerId, now));

        if (changedLocally > 0)
            await dbContext.SaveChangesAsync(cancellationToken);

        var syncedTaskIds = managerTasks
            .Where(t => t.TaskItemCreatedAt is not null)
            .Select(t => t.Id)
            .ToList();

        var reassignedInTasksModule = await taskReassigner.ReassignBySourceEntitiesAsync(
            companyId, syncedTaskIds, TaskSource.Offboarding, TaskActionType.Complete, resolvedManagerId,
            cancellationToken);

        if (changedLocally > 0 || reassignedInTasksModule > 0)
        {
            logger.LogInformation(
                "Re-pointed manager-assigned offboarding tasks for plan {OffboardingPlanId} " +
                "(employee {EmployeeId}, company {CompanyId}) to current manager {ManagerId}: " +
                "{LocalCount} local, {TasksModuleCount} in Tasks module.",
                planId, employeeId, companyId, resolvedManagerId, changedLocally, reassignedInTasksModule);
        }

        return Math.Max(changedLocally, reassignedInTasksModule);
    }
}
