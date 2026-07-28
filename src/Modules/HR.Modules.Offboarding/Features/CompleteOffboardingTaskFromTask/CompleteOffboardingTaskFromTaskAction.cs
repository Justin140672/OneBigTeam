using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;
using HR.Modules.Offboarding;

namespace HR.Modules.Offboarding.Features.CompleteOffboardingTaskFromTask;

internal sealed class CompleteOffboardingTaskFromTaskAction(
    OffboardingDbContext dbContext,
    IClock clock,
    IEmployeeNameReader employeeNameReader,
    INotificationWriter notificationWriter,
    ITaskCreator taskCreator,
    IAuditEventPublisher auditPublisher,
    IIntegrationEventPublisher integrationEventPublisher) : ITaskCompletionAction
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

        offboardingTask.Complete(clock.UtcNowOffset());

        var plan = await dbContext.OffboardingPlans
            .FirstOrDefaultAsync(p => p.Id == offboardingTask.OffboardingPlanId, cancellationToken);

        if (plan is null)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var now = clock.UtcNowOffset();

        var planTasks = await dbContext.OffboardingTasks
            .Where(t => t.OffboardingPlanId == plan.Id)
            .ToListAsync(cancellationToken);

        var isCompleting = plan.Status != OffboardingStatus.Completed
            && planTasks.Count > 0
            && planTasks.All(t => t.Status is OffboardingTaskStatus.Completed or OffboardingTaskStatus.Skipped);

        if (isCompleting)
            plan.Complete(now);

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
