using HR.Modules.Tasks.Contracts;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HR.Modules.Offboarding;

namespace HR.Modules.Offboarding.Features.CompleteOffboardingTaskFromTask;

internal sealed class CompleteOffboardingTaskFromTaskAction(
    OffboardingDbContext dbContext,
    IClock clock,
    IEmployeeNameReader employeeNameReader,
    INotificationWriter notificationWriter,
    ITaskCreator taskCreator,
    IAssetReturnService assetReturnService,
    IAuditEventPublisher auditPublisher,
    IIntegrationEventPublisher integrationEventPublisher,
    ILogger<CompleteOffboardingTaskFromTaskAction> logger) : ITaskCompletionAction
{
    private static readonly Guid SystemUserId = Guid.Empty;

    public TaskSource Source => TaskSource.Offboarding;
    public TaskActionType ActionType => TaskActionType.Complete;

    public async Task ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null)
            return;

        var offboardingTask = await dbContext.OffboardingTasks
            .FirstOrDefaultAsync(
                t => t.Id == context.SourceEntityId.Value && t.CompanyId == context.CompanyId,
                cancellationToken);

        if (offboardingTask is null)
            return;

        if (offboardingTask.Status is OffboardingTaskStatus.Completed or OffboardingTaskStatus.Skipped)
            return;

        var plan = await dbContext.OffboardingPlans
            .FirstOrDefaultAsync(p => p.Id == offboardingTask.OffboardingPlanId, cancellationToken);

        if (plan is null)
        {
            // No owning plan — nothing to reconcile the asset against, and nothing to gate for
            // completion. Fall back to the plain completion below (matches prior behaviour).
            offboardingTask.Complete(clock.UtcNowOffset());
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        // OFF-04: an asset-return checklist item must actually return (or explicitly write off) the
        // real Assets-module assignment before the offboarding side is allowed to consider it done.
        // This call is verified against the offboarding plan's own employee — it can never be used
        // to close out an assignment belonging to someone else, even if the underlying Tasks-module
        // TaskItem/sourceEntityId were somehow mismatched. The Tasks-module TaskItem itself has
        // already been marked Completed by the caller (see CompleteTaskHandler) before this
        // best-effort dispatch runs — that is an existing, unrelated architectural property of every
        // ITaskCompletionAction, not something introduced here. What this guards is the source of
        // truth that actually matters for offboarding: the OffboardingTask stays NOT Completed (and
        // therefore continues to block plan completion, see the "isCompleting" check below) whenever
        // the real asset return could not be verified/performed.
        if (offboardingTask.IsAssetReturnTask)
        {
            var outcome = context.OutcomeDecision switch
            {
                "Lost" => AssetReturnOutcome.Lost,
                "Damaged" => AssetReturnOutcome.Damaged,
                _ => AssetReturnOutcome.Returned
            };

            var returnResult = await assetReturnService.ReturnAsync(
                context.CompanyId,
                offboardingTask.AssetAssignmentId!.Value,
                expectedEmployeeId: plan.EmployeeId,
                outcome,
                returnedBy: context.CompletedBy,
                notes: context.OutcomeReason,
                cancellationToken);

            if (returnResult is AssetReturnResult.EmployeeMismatch or AssetReturnResult.NotFound)
            {
                logger.LogError(
                    "Offboarding asset-return task {OffboardingTaskId} (plan {OffboardingPlanId}, " +
                    "employee {EmployeeId}) could not be completed: asset assignment " +
                    "{AssetAssignmentId} returned {Result}. Leaving the offboarding task outstanding.",
                    offboardingTask.Id, plan.Id, plan.EmployeeId, offboardingTask.AssetAssignmentId, returnResult);
                return;
            }

            // Success or AlreadyReturned (the assignment was already closed by another path, e.g.
            // Assets' own "Request Return" flow, or a retried/duplicated completion) both mean the
            // real-world asset state is exactly what this offboarding task expects — safe to
            // complete the checklist item.
        }

        offboardingTask.Complete(clock.UtcNowOffset());

        var now = clock.UtcNowOffset();

        var planTasks = await dbContext.OffboardingTasks
            .Where(t => t.OffboardingPlanId == plan.Id)
            .ToListAsync(cancellationToken);

        var isCompleting = plan.Status != OffboardingStatus.Completed
            && planTasks.Count > 0
            && planTasks.All(t => t.Status is OffboardingTaskStatus.Completed or OffboardingTaskStatus.Skipped);

        if (isCompleting)
            plan.Complete(now);

        // OFF-05: once every task requiring explicit HR confirmation (backdated-departure asset/
        // document/access reconciliation) has reached a terminal state, the plan-level
        // RequiresHrReconciliation alert is no longer accurate and should clear. Checked against the
        // freshly-loaded planTasks (which already includes this task's just-applied Complete()), so
        // this reflects the state immediately after the current completion, not a stale snapshot.
        if (plan.RequiresHrReconciliation
            && planTasks.Where(t => t.RequiresHrConfirmation)
                .All(t => t.Status is OffboardingTaskStatus.Completed or OffboardingTaskStatus.Skipped))
        {
            plan.ResolveHrReconciliation(now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (isCompleting)
        {
            await CreateHrCompletionReviewTaskAsync(plan, cancellationToken);

            await auditPublisher.PublishAsync(new OffboardingPlanCompletedAuditEvent(
                plan.CompanyId,
                plan.Id,
                plan.EmployeeId,
                plan.LastWorkingDay,
                planTasks.Count,
                planTasks.Count(t => t.Status == OffboardingTaskStatus.Completed),
                planTasks.Count(t => t.Status == OffboardingTaskStatus.Skipped),
                now), cancellationToken);

            await integrationEventPublisher.PublishAsync(
                new OffboardingPlanCompletedIntegrationEvent(
                    plan.CompanyId,
                    plan.EmployeeId,
                    plan.Id,
                    now),
                cancellationToken);
        }
    }

    private async Task CreateHrCompletionReviewTaskAsync(OffboardingPlan plan, CancellationToken cancellationToken)
    {
        var names = await employeeNameReader.GetNamesAsync(plan.CompanyId, [plan.EmployeeId], cancellationToken);
        var employeeName = names.GetValueOrDefault(plan.EmployeeId, "Unknown Employee");

        await taskCreator.CreateAsync(
            plan.CompanyId,
            createdBy:          SystemUserId,
            title:              $"Offboarding completed — {employeeName}",
            description:        $"{employeeName}'s offboarding plan is complete. Review and close out any final steps.",
            priority:           TaskPriority.Medium,
            source:             TaskSource.Offboarding,
            actionType:         TaskActionType.Review,
            dueDate:            null,
            assignedEmployeeId: null,
            assignedUserId:     null,
            sourceEntityId:     plan.Id,
            cancellationToken);
    }
}
