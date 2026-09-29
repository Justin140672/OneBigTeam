using HR.Infrastructure.Abstractions;
using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Services;

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
            await integrationEventPublisher.PublishAsync(
                new OnboardingCompletedIntegrationEvent(plan.CompanyId, plan.EmployeeId, plan.Id, plan.UpdatedAt),
                cancellationToken);
        }

        return completedPlans.Count;
    }
}
