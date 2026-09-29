using HR.Infrastructure.Abstractions;
using HR.Modules.Offboarding.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Services;

internal sealed class OffboardingHistoryReplayer(
    OffboardingDbContext dbContext,
    IIntegrationEventPublisher integrationEventPublisher) : IOffboardingHistoryReplayer
{
    public async Task<int> ReplayStartedOffboardingsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var plans = await dbContext.OffboardingPlans
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        foreach (var plan in plans)
        {
            await integrationEventPublisher.PublishAsync(
                new OffboardingStartedIntegrationEvent(plan.CompanyId, plan.EmployeeId, plan.CreatedAt),
                cancellationToken);
        }

        return plans.Count;
    }
}
