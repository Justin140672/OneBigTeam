using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Persistence;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Features.CompleteOnboardingTaskFromTask;

internal sealed class CompleteOnboardingTaskFromTaskAction(
    OnboardingDbContext dbContext,
    IClock clock) : ITaskCompletionAction
{
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

        if (plan.Status == OnboardingStatus.NotStarted)
            plan.Start(now);

        var planTasks = await dbContext.OnboardingTasks
            .Where(t => t.OnboardingPlanId == plan.Id)
            .ToListAsync(cancellationToken);

        if (planTasks.Count > 0
            && planTasks.All(t => t.Status is OnboardingTaskStatus.Completed or OnboardingTaskStatus.Skipped))
        {
            plan.Complete(now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
