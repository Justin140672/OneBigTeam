using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Offboarding.Services;

/// <summary>
/// OFF-03: creates the Tasks-module TaskItem for every OffboardingTask that doesn't have one yet
/// (<see cref="OffboardingTask.TaskItemCreatedAt"/> is null). Used both immediately after a plan's
/// OffboardingTask rows are committed (StartOffboardingHandler) and by
/// <see cref="Jobs.OffboardingPlanCreationReconciliationJob"/> to retry any that failed the first
/// time. Never creates a general (Tasks-module) task before its OffboardingTask source row is
/// already durable — callers are required to have committed the OffboardingTask rows before
/// invoking this.
///
/// Per-task failures are isolated: one Tasks-module call throwing does not stop the remaining tasks
/// in the same plan from being attempted, and does not throw out of this method — the caller (a
/// user-facing request, or a background job) must not fail outright because of a partial
/// cross-module sync failure. What makes this NOT the "log-only and forget" anti-pattern the
/// failure is being fixed: any task left without a TaskItemCreatedAt stamp remains discoverable by
/// the reconciliation job (see the ix_offboarding_tasks_task_item_created_at index) and will be
/// retried until it succeeds.
/// </summary>
internal sealed class OffboardingTaskSynchronizer(
    OffboardingDbContext dbContext,
    ITaskCreator taskCreator,
    IClock clock,
    ILogger<OffboardingTaskSynchronizer> logger)
{
    public async Task<int> SyncPlanAsync(Guid companyId, Guid offboardingPlanId, CancellationToken cancellationToken)
    {
        var pendingTasks = await dbContext.OffboardingTasks
            .Where(t => t.CompanyId == companyId
                && t.OffboardingPlanId == offboardingPlanId
                && t.TaskItemCreatedAt == null
                && t.Status != OffboardingTaskStatus.Skipped
                && t.Status != OffboardingTaskStatus.Completed)
            .ToListAsync(cancellationToken);

        if (pendingTasks.Count == 0)
            return 0;

        var now = clock.UtcNowOffset();
        var syncedCount = 0;

        foreach (var task in pendingTasks)
        {
            try
            {
                await taskCreator.CreateAsync(
                    companyId,
                    createdBy:          OffboardingSystemActor.Id,
                    title:              task.Title,
                    description:        task.Description,
                    priority:           TaskPriority.Medium,
                    source:             TaskSource.Offboarding,
                    actionType:         TaskActionType.Complete,
                    dueDate:            task.DueDate,
                    assignedEmployeeId: task.AssignedEmployeeId,
                    assignedUserId:     task.AssignedEmployeeId,
                    sourceEntityId:     task.Id,
                    cancellationToken);

                task.MarkTaskItemCreated(now);
                syncedCount++;
            }
            catch (Exception ex)
            {
                // Isolated per task, on purpose — see class remarks. Logged at Error (not Warning):
                // this is a real, actionable gap until the reconciliation job retries it, not a
                // benign/expected condition.
                logger.LogError(
                    ex,
                    "Failed to create Tasks-module TaskItem for offboarding task {OffboardingTaskId} " +
                    "(plan {OffboardingPlanId}, company {CompanyId}). Will be retried by " +
                    "OffboardingPlanCreationReconciliationJob.",
                    task.Id, offboardingPlanId, companyId);
            }
        }

        if (syncedCount > 0)
            await dbContext.SaveChangesAsync(cancellationToken);

        return syncedCount;
    }
}
