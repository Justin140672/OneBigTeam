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
    // OFF-07: how far after the plan's LastWorkingDay the final HR completion-review task falls due.
    // Chosen so the review always has a concrete, trackable due date (feeding the same generic
    // Tasks-module overdue mechanism every other due-dated task already uses) rather than sitting in
    // a queue indefinitely with no due date — "cannot become invisible" per OFF-07's acceptance
    // criteria. 3 working-adjacent days gives HR a short, deliberate window to sign off after every
    // mandatory task has actually been completed.
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
        if (offboardingTask.Status is OffboardingTaskStatus.Completed or OffboardingTaskStatus.Skipped)
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

        // OFF-07: an explicit "Skip" outcome, mirroring the existing "Lost"/"Damaged" asset-return
        // outcome extension pattern below — the generic Tasks-module completion payload
        // (OutcomeDecision/OutcomeReason) already carries exactly what's needed. A reason is
        // mandatory; context.CompletedBy is resolved server-side by CompleteTaskHandler from the
        // authenticated caller's claim, never client-supplied, so it is safe to use directly as the
        // skip actor.
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

            // OFF-08: task-level audit entry — published once per task, guarded by the
            // Status-already-terminal early-return at the top of this method, which prevents a
            // redelivered/retried completion action from ever reaching here twice for the same
            // OffboardingTask. EmployeeId is the leaving employee the plan belongs to (falls back to
            // the task's own AssignedEmployeeId in the unexpected case the owning plan cannot be
            // found — mirrors the plain-completion fallback below).
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
            // No owning plan — nothing to reconcile the asset against, and nothing to gate for
            // completion. Fall back to the plain completion below (matches prior behaviour).
            offboardingTask.Complete(clock.UtcNowOffset());
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success();
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
        // therefore continues to block plan completion, see TryCompletePlanAsync) whenever the real
        // asset return could not be verified/performed.
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

            // Success or AlreadyReturned (the assignment was already closed by another path, e.g.
            // Assets' own "Request Return" flow, or a retried/duplicated completion) both mean the
            // real-world asset state is exactly what this offboarding task expects — safe to
            // complete the checklist item.
        }

        offboardingTask.Complete(clock.UtcNowOffset());
        await dbContext.SaveChangesAsync(cancellationToken);

        // OFF-08: task-level audit entry — see the Skip branch above for the idempotency reasoning
        // (guarded by the Status-already-terminal early-return at the top of this method).
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

    // OFF-07: shared tail for both the ordinary Complete path and the new Skip path — either
    // transition can be the one that resolves the plan's last outstanding mandatory task. Wraps the
    // task-status save and the plan-completion decision in a single explicit transaction that takes
    // a row lock (`SELECT ... FOR UPDATE`) on the owning plan for its duration. That lock is what
    // makes this safe under concurrency: if two of the plan's last mandatory tasks are completed at
    // the same moment (different requests, different OffboardingTask rows, same plan), the second
    // request's lock acquisition blocks until the first transaction commits — so it re-reads the
    // sibling tasks' post-commit state below and correctly sees the plan is already resolved, rather
    // than racing the first request to independently conclude "I'm the one who completes this plan"
    // and duplicating the plan-completed event and the HR review task.
    private async Task TryCompletePlanAsync(
        OffboardingTask offboardingTask,
        CancellationToken cancellationToken,
        OffboardingPlan? plan = null,
        Guid actorEmployeeId = default)
    {
        // OFF-07: transactions and "SELECT ... FOR UPDATE" row locking are only meaningful (and only
        // supported) against a real relational provider — the module's unit test suite runs against
        // EF Core's InMemory provider, which doesn't support transactions at all and has no
        // concurrent-request scenario to protect against anyway (each test is single-threaded against
        // its own isolated in-memory database). IsRelational() is false there, so those tests exercise
        // the same domain/orchestration logic without the Postgres-specific locking step; the real
        // concurrency guarantee is covered by the integration test suite against real Postgres.
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
                // Row lock: serialises every concurrent completion/skip for this specific plan. Cheap
                // and short-lived — held only for the remainder of this transaction, which never
                // makes an external (cross-module/network) call before committing.
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT id FROM offboarding.offboarding_plans WHERE id = {plan.Id} FOR UPDATE",
                    cancellationToken);

                // OFF-07: the lock above serialises concurrent completions, but this `plan` entity
                // was materialised before we acquired the lock. Reload it so Status and
                // FinalReviewTaskCreatedAt reflect whatever a concurrent completion committed while
                // we were blocked — otherwise the stale in-memory Status lets the second request
                // independently "complete" the plan again and claim a duplicate HR review task.
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
            // OFF-08: whoever completed/skipped the specific task that resolved the plan — matches
            // this plan-completed event to the person who actually caused it, never assumed to be
            // the affected employee. Falls back to OffboardingSystemActor.Id (Guid.Empty) only if
            // this method is ever reached without an actor (defensive; every call site above
            // always supplies context.CompletedBy).
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
            return; // Not this task's completion that finished the plan — nothing owed here.

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
            planTasks.Count(t => t.Status == OffboardingTaskStatus.Skipped),
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

        // OFF-07: mandatory tasks must reach Completed — a mandatory task that is Skipped is still an
        // unresolved material exit obligation and keeps blocking completion. Optional tasks may be
        // either Completed or Skipped. Replaces the former "all tasks Completed or Skipped" rule,
        // which treated every task as equally skippable regardless of materiality.
        var isCompleting = plan.Status != OffboardingStatus.Completed
            && OffboardingPlan.CanComplete(planTasks);

        var reviewTaskClaimed = false;
        if (isCompleting)
        {
            plan.Complete(now);
            reviewTaskClaimed = plan.TryClaimFinalReviewTaskCreation(now);
        }

        // OFF-05: once every task requiring explicit HR confirmation (backdated-departure asset/
        // document/access reconciliation) has reached a terminal state, the plan-level
        // RequiresHrReconciliation alert is no longer accurate and should clear.
        if (plan.RequiresHrReconciliation
            && planTasks.Where(t => t.RequiresHrConfirmation)
                .All(t => t.Status is OffboardingTaskStatus.Completed or OffboardingTaskStatus.Skipped))
        {
            plan.ResolveHrReconciliation(now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new OffboardingPlanCompletionOutcome(
            isCompleting,
            reviewTaskClaimed,
            planTasks.Count,
            planTasks.Count(t => t.Status == OffboardingTaskStatus.Completed),
            planTasks.Count(t => t.Status == OffboardingTaskStatus.Skipped),
            now);
    }

    // OFF-07: always created (never conditionally skippable itself) and always assigned to a single,
    // deterministic HR administrator (lowest Guid) — same resolution pattern as
    // StartOffboardingHandler's reconciliation assignee and Probation's ProbationReviewAssignment,
    // given the Tasks module's single-assignee model. Given a due date (LastWorkingDay +
    // FinalReviewDueDateOffsetDays) so it feeds the existing generic Tasks-module overdue
    // notification mechanism rather than sitting invisibly in an unbounded queue. Every HR
    // administrator additionally receives an in-app notification, so the review is never dependent
    // on exactly one person noticing their task list.
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
                continue; // Already notified via CreateAsync's own "New task assigned" notification.

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
