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
    IHrAdministratorDirectory hrAdministratorDirectory,
    IAuditEventPublisher auditPublisher,
    IIntegrationEventPublisher integrationEventPublisher,
    ILogger<CompleteOffboardingTaskFromTaskAction> logger) : ITaskCompletionAction
{
    private const int FinalReviewDueDateOffsetDays = 3;

    public TaskSource Source => TaskSource.Offboarding;
    public TaskActionType ActionType => TaskActionType.Complete;

    public async Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null)
            return Result.Failure(Error.Validation("This task has no associated offboarding task."));

        var offboardingTask = await dbContext.OffboardingTasks
            .FirstOrDefaultAsync(
                t => t.Id == context.SourceEntityId.Value && t.CompanyId == context.CompanyId,
                cancellationToken);

        if (offboardingTask is null)
            return Result.Failure(Error.NotFound("The associated offboarding task was not found."));

        // Ticket 15 (P1): "already resolved" (e.g. a retried/duplicated dispatch, or a reconciliation
        // sweep replaying an interrupted operation) proves only that THIS task's own status
        // transition committed — it says nothing about whether the plan-level follow-ups
        // (final review task, audit, integration event) that TryCompletePlanAsync fires AFTER that
        // commit ever ran. Recover them instead of assuming task-level completion implies the whole
        // dispatch, including plan-level effects, is done.
        if (offboardingTask.Status is OffboardingTaskStatus.Completed or OffboardingTaskStatus.Skipped
            or OffboardingTaskStatus.Waived or OffboardingTaskStatus.Cancelled)
        {
            // Recovery only applies to a genuine Tasks-dispatch replay (identified by a stable
            // DispatchOperationId — ticket 11), never to an arbitrary already-resolved call with no
            // dispatch identity — see CompleteOnboardingTaskFromTaskAction's identical guard for the
            // full reasoning (a long-settled plan reached its current state via some unrelated path
            // must not have its audit/review-task effects unconditionally re-attempted on every
            // later no-op call).
            if (context.DispatchOperationId != Guid.Empty)
            {
                var existingPlan = await dbContext.OffboardingPlans
                    .FirstOrDefaultAsync(p => p.Id == offboardingTask.OffboardingPlanId, cancellationToken);

                if (existingPlan is not null)
                    await RecoverPlanCompletionEffectsAsync(existingPlan, cancellationToken);
            }

            return Result.Success();
        }

        var plan = await dbContext.OffboardingPlans
            .FirstOrDefaultAsync(p => p.Id == offboardingTask.OffboardingPlanId, cancellationToken);

        if (context.OutcomeDecision == "Skip")
        {
            if (string.IsNullOrWhiteSpace(context.OutcomeReason))
            {
                logger.LogError(
                    "Offboarding task {OffboardingTaskId} could not be skipped: no reason was " +
                    "supplied. Leaving the offboarding task outstanding.",
                    offboardingTask.Id);
                return Result.Failure(Error.Validation("A reason is required to skip this task."));
            }

            offboardingTask.Skip(clock.UtcNowOffset(), context.OutcomeReason, context.CompletedBy);
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditPublisher.PublishAsync(
                new OffboardingTaskSkippedAuditEvent(
                    offboardingTask.CompanyId,
                    offboardingTask.OffboardingPlanId,
                    offboardingTask.Id,
                    context.TaskId,
                    plan?.EmployeeId ?? offboardingTask.AssignedEmployeeId ?? context.CompletedBy,
                    context.CompletedBy,
                    offboardingTask.Title,
                    offboardingTask.SkipReason!,
                    offboardingTask.AssetAssignmentId,
                    offboardingTask.SkippedAt!.Value),
                cancellationToken);

            if (plan is not null)
                await TryCompletePlanAsync(offboardingTask, cancellationToken, plan, context.CompletedBy);
            return Result.Success();
        }

        if (plan is null)
        {
            offboardingTask.Complete(clock.UtcNowOffset());
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

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
                cancellationToken,
                dispatchOperationId: context.DispatchOperationId);

            if (returnResult is AssetReturnResult.EmployeeMismatch or AssetReturnResult.NotFound)
            {
                logger.LogError(
                    "Offboarding asset-return task {OffboardingTaskId} (plan {OffboardingPlanId}, " +
                    "employee {EmployeeId}) could not be completed: asset assignment " +
                    "{AssetAssignmentId} returned {Result}. Leaving the offboarding task outstanding.",
                    offboardingTask.Id, plan.Id, plan.EmployeeId, offboardingTask.AssetAssignmentId, returnResult);
                return Result.Failure(Error.Validation(
                    $"The asset assignment could not be returned ({returnResult})."));
            }

        }

        offboardingTask.Complete(clock.UtcNowOffset());
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new OffboardingTaskCompletedAuditEvent(
                offboardingTask.CompanyId,
                offboardingTask.OffboardingPlanId,
                offboardingTask.Id,
                context.TaskId,
                plan.EmployeeId,
                context.CompletedBy,
                offboardingTask.Title,
                offboardingTask.AssetAssignmentId,
                offboardingTask.CompletedAt!.Value),
            cancellationToken);

        await TryCompletePlanAsync(offboardingTask, cancellationToken, plan, context.CompletedBy);

        return Result.Success();
    }

    private async Task TryCompletePlanAsync(
        OffboardingTask offboardingTask,
        CancellationToken cancellationToken,
        OffboardingPlan? plan = null,
        Guid actorEmployeeId = default)
    {
        var isRelational = dbContext.Database.IsRelational();

        var transaction = isRelational
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        OffboardingPlanCompletionOutcome outcome;

        try
        {
            plan ??= await dbContext.OffboardingPlans
                .FirstOrDefaultAsync(p => p.Id == offboardingTask.OffboardingPlanId, cancellationToken);

            if (plan is null)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (isRelational)
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT id FROM offboarding.offboarding_plans WHERE id = {plan.Id} FOR UPDATE",
                    cancellationToken);

                await dbContext.Entry(plan).ReloadAsync(cancellationToken);
            }

            outcome = await ApplyPlanCompletionAsync(plan, cancellationToken);

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }

        if (!outcome.IsCompleting)
            return;

        // Cross-module/external side effects (task creation, notifications, audit, integration
        // events) deliberately happen after the transaction above has committed — none of them
        // should hold the plan row lock open, and none of them should be able to roll back the
        // already-committed plan/task state if they fail.
        if (outcome.ReviewTaskClaimed)
            await CreateHrCompletionReviewTaskAsync(plan, cancellationToken);

        await auditPublisher.PublishAsync(new OffboardingPlanCompletedAuditEvent(
            plan.CompanyId,
            plan.Id,
            plan.EmployeeId,
            actorEmployeeId == default ? OffboardingSystemActor.Id : actorEmployeeId,
            plan.LastWorkingDay,
            outcome.TotalTasks,
            outcome.CompletedTasks,
            outcome.SkippedTasks,
            outcome.OccurredAt), cancellationToken);

        await integrationEventPublisher.PublishAsync(
            new OffboardingPlanCompletedIntegrationEvent(
                plan.CompanyId,
                plan.EmployeeId,
                plan.Id,
                outcome.OccurredAt),
            cancellationToken);
    }

    /// <summary>
    /// Ticket 15 (P1): re-derives which plan-completion follow-ups are still owed purely from the
    /// plan's own persisted state, for the case where a prior dispatch already committed the task
    /// (and possibly the plan) transition but was interrupted before TryCompletePlanAsync's
    /// post-commit effects (review task, audit, integration event) ran. Every effect here is itself
    /// idempotent under replay — <see cref="OffboardingPlan.TryClaimFinalReviewTaskCreation"/> is a
    /// durable one-shot claim, the audit event's EventId is deterministic, and integration-event
    /// consumers are required to be idempotent — so calling this unconditionally whenever the task
    /// was already resolved can never duplicate any of them.
    /// </summary>
    private async Task RecoverPlanCompletionEffectsAsync(OffboardingPlan plan, CancellationToken cancellationToken)
    {
        if (plan.Status != OffboardingStatus.Completed)
            return;

        var now = clock.UtcNowOffset();
        var reviewTaskClaimed = plan.TryClaimFinalReviewTaskCreation(now);
        if (reviewTaskClaimed)
            await dbContext.SaveChangesAsync(cancellationToken);

        if (reviewTaskClaimed)
            await CreateHrCompletionReviewTaskAsync(plan, cancellationToken);

        var planTasks = await dbContext.OffboardingTasks
            .Where(t => t.OffboardingPlanId == plan.Id)
            .ToListAsync(cancellationToken);

        await auditPublisher.PublishAsync(new OffboardingPlanCompletedAuditEvent(
            plan.CompanyId,
            plan.Id,
            plan.EmployeeId,
            OffboardingSystemActor.Id,
            plan.LastWorkingDay,
            planTasks.Count,
            planTasks.Count(t => t.Status == OffboardingTaskStatus.Completed),
            planTasks.Count(t => t.Status is OffboardingTaskStatus.Skipped or OffboardingTaskStatus.Waived),
            now), cancellationToken);

        await integrationEventPublisher.PublishAsync(
            new OffboardingPlanCompletedIntegrationEvent(plan.CompanyId, plan.EmployeeId, plan.Id, now),
            cancellationToken);
    }

    private readonly record struct OffboardingPlanCompletionOutcome(
        bool IsCompleting,
        bool ReviewTaskClaimed,
        int TotalTasks,
        int CompletedTasks,
        int SkippedTasks,
        DateTimeOffset OccurredAt);

    private async Task<OffboardingPlanCompletionOutcome> ApplyPlanCompletionAsync(
        OffboardingPlan plan, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var planTasks = await dbContext.OffboardingTasks
            .Where(t => t.OffboardingPlanId == plan.Id)
            .ToListAsync(cancellationToken);

        var isCompleting = plan.Status != OffboardingStatus.Completed
            && OffboardingPlan.CanComplete(planTasks);

        var reviewTaskClaimed = false;
        if (isCompleting)
        {
            plan.Complete(now);
            reviewTaskClaimed = plan.TryClaimFinalReviewTaskCreation(now);
        }

        if (plan.RequiresHrReconciliation
            && planTasks.Where(t => t.RequiresHrConfirmation)
                .All(t => t.Status is OffboardingTaskStatus.Completed or OffboardingTaskStatus.Skipped
                or OffboardingTaskStatus.Waived or OffboardingTaskStatus.Cancelled))
        {
            plan.ResolveHrReconciliation(now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new OffboardingPlanCompletionOutcome(
            isCompleting,
            reviewTaskClaimed,
            planTasks.Count,
            planTasks.Count(t => t.Status == OffboardingTaskStatus.Completed),
            planTasks.Count(t => t.Status is OffboardingTaskStatus.Skipped or OffboardingTaskStatus.Waived),
            now);
    }

    private async Task CreateHrCompletionReviewTaskAsync(OffboardingPlan plan, CancellationToken cancellationToken)
    {
        var names = await employeeNameReader.GetNamesAsync(plan.CompanyId, [plan.EmployeeId], cancellationToken);
        var employeeName = names.GetValueOrDefault(plan.EmployeeId, "Unknown Employee");

        var hrAdministratorIds = await hrAdministratorDirectory.GetHrAdministratorEmployeeIdsAsync(
            plan.CompanyId, cancellationToken);
        var reviewAssigneeId = hrAdministratorIds.Count == 0
            ? (Guid?)null
            : hrAdministratorIds.OrderBy(id => id).First();

        var dueDate = plan.LastWorkingDay.AddDays(FinalReviewDueDateOffsetDays);

        await taskCreator.CreateAsync(
            plan.CompanyId,
            createdBy:          OffboardingSystemActor.Id,
            title:              $"Offboarding completed — {employeeName}",
            description:        $"{employeeName}'s offboarding plan is complete. Review and close out any final steps.",
            priority:           TaskPriority.High,
            source:             TaskSource.Offboarding,
            actionType:         TaskActionType.Review,
            dueDate:            dueDate,
            assignedEmployeeId: reviewAssigneeId,
            assignedUserId:     null,
            sourceEntityId:     plan.Id,
            cancellationToken,
            // Ticket 15 (P1): defense-in-depth alongside plan.TryClaimFinalReviewTaskCreation — a
            // stable per-plan key so even a caller that somehow reached this method twice for the
            // same plan (e.g. a bug in the claim call site) cannot create a second review task.
            idempotencyKey: $"OffboardingPlanCompleted:{plan.Id}");

        var now = clock.UtcNowOffset();

        foreach (var hrAdministratorId in hrAdministratorIds)
        {
            if (hrAdministratorId == reviewAssigneeId)
                continue;

            await notificationWriter.WriteAsync(
                Guid.NewGuid(), plan.CompanyId, hrAdministratorId,
                $"Offboarding completed — {employeeName}",
                $"{employeeName}'s offboarding plan is complete and ready for final HR review.",
                plan.Id,
                NotificationType.OffboardingCompleted,
                NotificationPriority.Normal,
                now,
                cancellationToken);
        }
    }
}
