using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Persistence;
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
    IAuditEventPublisher auditPublisher) : ITaskCompletionAction
{
    private static readonly Guid SystemUserId = Guid.Empty;

    public TaskSource Source => TaskSource.Onboarding;
    public TaskActionType ActionType => TaskActionType.Complete;

    public async Task ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null)
            return;

        var onboardingTask = await dbContext.OnboardingTasks
            .FirstOrDefaultAsync(
                t => t.Id == context.SourceEntityId.Value && t.CompanyId == context.CompanyId,
                cancellationToken);

        if (onboardingTask is null)
            return;

        if (onboardingTask.Status is OnboardingTaskStatus.Completed or OnboardingTaskStatus.Skipped)
            return;

        onboardingTask.Complete(clock.UtcNowOffset());

        var plan = await dbContext.OnboardingPlans
            .FirstOrDefaultAsync(p => p.Id == onboardingTask.OnboardingPlanId, cancellationToken);

        if (plan is null)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var now = clock.UtcNowOffset();

        var isStarting = plan.Status == OnboardingStatus.NotStarted;
        if (isStarting)
            plan.Start(now);

        var planTasks = await dbContext.OnboardingTasks
            .Where(t => t.OnboardingPlanId == plan.Id)
            .ToListAsync(cancellationToken);

        var isCompleting = planTasks.Count > 0
            && planTasks.All(t => t.Status is OnboardingTaskStatus.Completed or OnboardingTaskStatus.Skipped);

        if (isCompleting)
            plan.Complete(now);

        await dbContext.SaveChangesAsync(cancellationToken);

        if (isStarting)
            await NotifyOnboardingStartedAsync(plan, now, cancellationToken);

        if (isCompleting)
        {
            await CreateHrCompletionReviewTaskAsync(plan, cancellationToken);

            await auditPublisher.PublishAsync(new OnboardingPlanCompletedAuditEvent(
                plan.CompanyId,
                plan.Id,
                plan.EmployeeId,
                plan.StartDate,
                planTasks.Count,
                planTasks.Count(t => t.Status == OnboardingTaskStatus.Completed),
                planTasks.Count(t => t.Status == OnboardingTaskStatus.Skipped),
                now), cancellationToken);
        }
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
            cancellationToken);
    }
}
