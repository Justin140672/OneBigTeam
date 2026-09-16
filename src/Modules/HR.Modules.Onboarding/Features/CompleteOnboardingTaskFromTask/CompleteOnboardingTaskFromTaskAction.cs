using HR.Modules.Tasks.Contracts;
using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Features.CompleteOnboardingTaskFromTask;

internal sealed class CompleteOnboardingTaskFromTaskAction(
    OnboardingDbContext dbContext,
    IClock clock,
    IManagerReader managerReader,
    IEmployeeNameReader employeeNameReader,
    INotificationWriter notificationWriter,
    ITaskCreator taskCreator,
    IAuditEventPublisher auditPublisher,
    IIntegrationEventPublisher integrationEventPublisher) : ITaskCompletionAction
{
    private static readonly Guid SystemUserId = Guid.Empty;

    public TaskSource Source => TaskSource.Onboarding;
    public TaskActionType ActionType => TaskActionType.Complete;

    public async Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null)
            return Result.Failure(Error.Validation("This task has no associated onboarding task."));

        var onboardingTask = await dbContext.OnboardingTasks
            .FirstOrDefaultAsync(
                t => t.Id == context.SourceEntityId.Value && t.CompanyId == context.CompanyId,
                cancellationToken);

        if (onboardingTask is null)
            return Result.Failure(Error.NotFound("The associated onboarding task was not found."));

        var now = clock.UtcNowOffset();

        // Ticket 15 (P1): "already resolved" (e.g. a retried/duplicated dispatch, or a reconciliation
        // sweep replaying an interrupted operation) proves only that THIS task's primary mutation
        // committed — it says nothing about whether the plan-level follow-up effects (start
        // notification, completion review task, audit, integration event) that a prior attempt may
        // have been interrupted before reaching were ever applied. Recover them instead of assuming
        // completion of the domain entity implies completion of the whole dispatch.
        if (onboardingTask.Status is OnboardingTaskStatus.Completed or OnboardingTaskStatus.Skipped)
        {
            // Recovery only applies to a genuine Tasks-dispatch replay (identified by a stable
            // DispatchOperationId — see ticket 11) — never to an arbitrary "already resolved" call
            // with no dispatch identity. Without that identity there is no reliable way to
            // distinguish "this task's own completion was interrupted before its plan-level effects
            // ran" from "this plan reached its current state via some unrelated, long-settled path"
            // (e.g. a plan that started days ago before this task was ever touched) — treating every
            // such call as recoverable would resend "onboarding started" on every later no-op replay
            // of an unrelated already-resolved task. This mirrors the same
            // `DispatchOperationId == Guid.Empty` guard used by LeaveTaskCompletionAction/
            // AssetTaskCompletionAction and CompleteProbationReviewFromTaskAction's Extend recovery.
            if (context.DispatchOperationId != Guid.Empty)
            {
                var existingPlan = await dbContext.OnboardingPlans
                    .FirstOrDefaultAsync(p => p.Id == onboardingTask.OnboardingPlanId, cancellationToken);

                if (existingPlan is not null)
                    await RecoverPlanEffectsAsync(existingPlan, context, now, cancellationToken);
            }

            return Result.Success();
        }

        onboardingTask.Complete(now);

        var plan = await dbContext.OnboardingPlans
            .FirstOrDefaultAsync(p => p.Id == onboardingTask.OnboardingPlanId, cancellationToken);

        if (plan is null)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        var isStarting = plan.Status == OnboardingStatus.NotStarted;
        if (isStarting)
            plan.Start(now);

        var planTasks = await dbContext.OnboardingTasks
            .Where(t => t.OnboardingPlanId == plan.Id)
            .ToListAsync(cancellationToken);

        var isCompleting = plan.Status != OnboardingStatus.Completed
            && planTasks.Count > 0
            && planTasks.All(t => t.Status is OnboardingTaskStatus.Completed or OnboardingTaskStatus.Skipped);

        if (isCompleting)
            plan.Complete(now);

        // Primary mutation commits first (task + plan transition). Everything below is a durably
        // recoverable follow-up: every effect carries a deterministic key (task-creation
        // idempotency key, notification's own (employee, source, type) uniqueness, or the audit
        // event's deterministic EventId), so an interruption between here and the end of this method
        // is safely recovered by RecoverPlanEffectsAsync (see the already-resolved branch above) on
        // the next dispatch of this SAME operation. On the normal, uninterrupted path each effect is
        // still gated by its own fresh transition flag (isStarting/isCompleting) rather than fired
        // unconditionally, so an ordinary completion of an unrelated task never re-sends the "plan
        // started" notification.
        await dbContext.SaveChangesAsync(cancellationToken);

        if (isStarting)
            await NotifyOnboardingStartedAsync(plan, now, cancellationToken);

        if (isCompleting)
            await ApplyPlanCompletedEffectsAsync(plan, context, now, cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Ticket 15 (P1): re-derives which plan-level effects are owed purely from the plan's own
    /// persisted state (not from a transient "did I just flip this in memory" flag, which goes stale
    /// the moment a prior attempt already committed the transition and gets interrupted before
    /// reaching this point). Only reached from the already-resolved short-circuit — the normal path
    /// above still gates each effect on its own fresh transition flag, so a routine completion of an
    /// unrelated task never re-attempts these. Every effect here is itself idempotent under replay
    /// (notification's own (employee, source, type) uniqueness, task-creation idempotency key, or
    /// the audit event's deterministic EventId), so calling this unconditionally on replay can never
    /// duplicate a notification, review task, audit event, or integration event.
    /// </summary>
    private async Task RecoverPlanEffectsAsync(
        OnboardingPlan plan, TaskCompletionContext context, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (plan.Status is OnboardingStatus.InProgress or OnboardingStatus.Completed)
        {
            // Checked explicitly (rather than relying solely on NotificationWriter's own unique-key
            // dedupe) so a normal, uninterrupted replay of an already-fully-applied completion sends
            // no notification at all, not merely "no duplicate row" — the employee-facing
            // notification is always written when the plan starts, so its presence is a reliable
            // proxy for "the start effect already ran".
            var alreadyNotified = await notificationWriter.ExistsAsync(
                plan.EmployeeId, plan.Id, NotificationType.OnboardingStarted, cancellationToken);
            if (!alreadyNotified)
                await NotifyOnboardingStartedAsync(plan, now, cancellationToken);
        }

        if (plan.Status == OnboardingStatus.Completed)
            await ApplyPlanCompletedEffectsAsync(plan, context, now, cancellationToken);
    }

    private async Task ApplyPlanCompletedEffectsAsync(
        OnboardingPlan plan, TaskCompletionContext context, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var planTasks = await dbContext.OnboardingTasks
            .Where(t => t.OnboardingPlanId == plan.Id)
            .ToListAsync(cancellationToken);

        await CreateHrCompletionReviewTaskAsync(plan, cancellationToken);

        await auditPublisher.PublishAsync(new OnboardingPlanCompletedAuditEvent(
            plan.CompanyId,
            plan.Id,
            plan.EmployeeId,
            plan.StartDate,
            planTasks.Count,
            planTasks.Count(t => t.Status == OnboardingTaskStatus.Completed),
            planTasks.Count(t => t.Status == OnboardingTaskStatus.Skipped),
            now,
            context.CompletedBy), cancellationToken);

        // Cross-module delivery — per architecture, downstream consumers of an integration event
        // must already be idempotent under repeated delivery, so an unconditional re-publish here on
        // recovery is safe by contract, not merely by convention.
        await integrationEventPublisher.PublishAsync(
            new OnboardingCompletedIntegrationEvent(plan.CompanyId, plan.EmployeeId, plan.Id, now),
            cancellationToken);
    }

    private async Task NotifyOnboardingStartedAsync(OnboardingPlan plan, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var names = await employeeNameReader.GetNamesAsync(plan.CompanyId, [plan.EmployeeId], cancellationToken);
        var employeeName = names.GetValueOrDefault(plan.EmployeeId, "the new employee");

        var managerId = await managerReader.GetManagerIdAsync(plan.CompanyId, plan.EmployeeId, cancellationToken);
        if (managerId.HasValue)
        {
            await notificationWriter.WriteAsync(
                Guid.NewGuid(), plan.CompanyId, managerId.Value,
                $"Onboarding started for {employeeName}",
                $"{employeeName}'s onboarding plan has been created with their start-date tasks. Review their checklist.",
                plan.Id,
                NotificationType.OnboardingStarted,
                NotificationPriority.Normal,
                now,
                cancellationToken);
        }

        await notificationWriter.WriteAsync(
            Guid.NewGuid(), plan.CompanyId, plan.EmployeeId,
            "Your onboarding has started",
            "Your onboarding checklist has been created — check your tasks to get started.",
            plan.Id,
            NotificationType.OnboardingStarted,
            NotificationPriority.Normal,
            now,
            cancellationToken);
    }

    private async Task CreateHrCompletionReviewTaskAsync(OnboardingPlan plan, CancellationToken cancellationToken)
    {
        var names = await employeeNameReader.GetNamesAsync(plan.CompanyId, [plan.EmployeeId], cancellationToken);
        var employeeName = names.GetValueOrDefault(plan.EmployeeId, "Unknown Employee");

        await taskCreator.CreateAsync(
            plan.CompanyId,
            createdBy:          SystemUserId,
            title:              $"Onboarding completed — {employeeName}",
            description:        $"{employeeName}'s onboarding plan is complete. Review and close out any final steps.",
            priority:           TaskPriority.Medium,
            source:             TaskSource.Onboarding,
            actionType:         TaskActionType.Review,
            dueDate:            null,
            assignedEmployeeId: null,
            assignedUserId:     null,
            sourceEntityId:     plan.Id,
            cancellationToken,
            // Ticket 15 (P1): stable per-plan key so a recovered/replayed dispatch for the same
            // completion never creates a second "Onboarding completed" review task — enforced by
            // ITaskCreator's (company_id, idempotency_key) database uniqueness constraint.
            idempotencyKey: $"OnboardingPlanCompleted:{plan.Id}");
    }
}
