using HR.Infrastructure.Abstractions;
using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Services;

// Historical replay counterpart to CompleteOnboardingTaskFromTaskAction: that handler publishes
// OnboardingCompletedIntegrationEvent only when an OnboardingPlan transitions to Completed (via
// OnboardingPlan.Complete). This replayer targets exactly the same condition — every
// OnboardingPlan currently in the Completed status — for plans that finished before the employee
// timeline feature existed.
internal sealed class OnboardingHistoryReplayer(
    OnboardingDbContext dbContext,
    IIntegrationEventPublisher integrationEventPublisher) : IOnboardingHistoryReplayer
{
    public async Task<int> ReplayOnboardingCompletedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var completedPlans = await dbContext.OnboardingPlans
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.Status == OnboardingStatus.Completed)
            .ToListAsync(cancellationToken);

        foreach (var plan in completedPlans)
        {
            // UpdatedAt is bumped by OnboardingPlan.Complete(now) at the moment of completion, the
            // same `now` the live handler passes as OccurredAt — an exact match, not a fallback.
            await integrationEventPublisher.PublishAsync(
                new OnboardingCompletedIntegrationEvent(plan.CompanyId, plan.EmployeeId, plan.Id, plan.UpdatedAt),
                cancellationToken);
        }

        return completedPlans.Count;
    }
}
